"""Cloud krok 12: záloha a obnova dat MiniShopu do/z OCI Object Storage.
Obsah zálohy: snímek PostgreSQL (minishop.pgdump), Data Protection klíče, obrázky produktů.

Běží v kontejneru ghcr.io/oracle/oci-cli (Python + OCI CLI) – spouští ho backup.sh / restore.sh na VM:
  python3 backup.py backup              → snímek + klíče + obrázky → minishop-RRRRMMDDTHHMMSSZ.tar.gz → bucket
  python3 backup.py fetch latest|<jméno>|/restore-in/<soubor> <složka>
                                        → stáhne zálohu a rozbalí ji do <složka>/content (nic nemění)
  python3 backup.py restore-files <složka> → klíče a obrázky z rozbalené zálohy zpět do volumes (služby musí stát)
  python3 backup.py list                → seznam záloh v bucketu
Škálování krok 4: obnovu řídí restore.sh – fetch → ověření a obnova PostgreSQL (pg_restore) → restore-files.

Snímek databáze dělá backup.sh (pg_dump v kontejneru postgres – konzistentní stav k jednomu okamžiku i za běhu)
a sem ho připojí jako soubor. Do škálování kroku 3C se zálohovaly i SQLite soubory (*.db) – starší zálohy je
ještě mají, obnova je přeskočí.

Přihlášení k OCI: instance principal – VM má identitu (dynamická skupina), žádný klíč ani heslo na disku.
Smí do bucketu zapisovat a číst, ale NE mazat (politika v infra/backup.tf); staré zálohy maže lifecycle pravidlo.

Volumes (připojí backup.sh/restore.sh): /web = minishop_web-data, /inventory = minishop_inventory-data.
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone

BUCKET = os.environ.get("BACKUP_BUCKET", "minishop-backups")
PREFIX = "minishop-"

# Snímek PostgreSQL (pg_dump --format=custom, škálování krok 3A): vytvoří ho backup.sh, sem ho připojí jako soubor.
# RabbitMQ se nezálohuje – zprávy jsou dočasné, pravda je v outboxech v databázi.
PG_DUMP = ("/pgdump/minishop.pgdump", "minishop.pgdump")
# Složky s daty: (cesta ve volume, jméno v archivu). Chybějící složka se přeskočí – ve starších zálohách i na disku.
DIRECTORIES = [
    ("/web/keys", "keys"),  # Data Protection klíče Webu (cookies, antiforgery)
    ("/inventory/images", "images"),  # e-shop krok 2B: obrázky produktů (jména souborů jsou v databázi)
]
LOCAL_BACKUP_DIR = "/restore-in/"  # škálování krok 4: záloha z lokálního souboru (restore.sh ho sem připojí)


def log(message: str) -> None:
    print(f"[{datetime.now(timezone.utc):%Y-%m-%dT%H:%M:%SZ}] {message}", flush=True)


def oci(*args: str) -> str:
    """OCI CLI s přihlášením instance principal; vrací stdout (JSON)."""
    result = subprocess.run(["oci", *args, "--auth", "instance_principal"],
                            check=True, capture_output=True, text=True)
    return result.stdout


def backup() -> None:
    name = f"{PREFIX}{datetime.now(timezone.utc):%Y%m%dT%H%M%SZ}.tar.gz"
    with tempfile.TemporaryDirectory() as work:
        content = os.path.join(work, "content")
        os.makedirs(content)

        source, target = PG_DUMP
        if not os.path.isfile(source) or os.path.getsize(source) == 0:
            raise FileNotFoundError(f"Chybí snímek PostgreSQL {source} – spouští se záloha přes backup.sh?")
        shutil.copyfile(source, os.path.join(content, target))
        log(f"  {target}: {os.path.getsize(source)} B")

        for source, target in DIRECTORIES:
            if os.path.isdir(source):
                shutil.copytree(source, os.path.join(content, target))
                log(f"  {target}/: {len(os.listdir(source))} souborů")

        with open(os.path.join(content, "manifest.json"), "w") as manifest:
            json.dump({"created_utc": datetime.now(timezone.utc).isoformat(),
                       "files": sorted(os.listdir(content))}, manifest, indent=2)

        archive = os.path.join(work, name)
        with tarfile.open(archive, "w:gz") as tar:
            tar.add(content, arcname=".")

        # --no-multipart: jediný PUT (politika povoluje OBJECT_CREATE, ne vícedílné nahrávání).
        oci("os", "object", "put", "--bucket-name", BUCKET, "--name", name,
            "--file", archive, "--no-multipart")
        log(f"Záloha nahrána: {BUCKET}/{name} ({os.path.getsize(archive) // 1024} kB)")


def list_backups() -> list[str]:
    data = json.loads(oci("os", "object", "list", "--bucket-name", BUCKET,
                          "--prefix", PREFIX, "--all") or "{}")
    # Jméno obsahuje čas UTC ve formátu RRRRMMDDTHHMMSSZ → abecední pořadí = časové.
    return sorted(item["name"] for item in data.get("data", []))


def fetch(name: str, work: str) -> None:
    """Škálování krok 4: stáhne (nebo vezme lokální) zálohu a rozbalí ji do work/content.
    Nic v produkci nemění – restore.sh mezitím ověří snímek PostgreSQL a teprve pak se obnovuje."""
    if name.startswith(LOCAL_BACKUP_DIR) and os.path.isfile(name):
        archive = name
        log(f"Obnovuji z lokálního souboru {os.path.basename(name)}")
    else:
        if name != "latest" and not re.fullmatch(r"minishop-\d{8}T\d{6}Z\.tar\.gz", name):
            raise ValueError(f"Čekám 'latest' nebo jméno zálohy (minishop-RRRRMMDDTHHMMSSZ.tar.gz), ne '{name}'.")
        if name == "latest":
            backups = list_backups()
            if not backups:
                raise RuntimeError(f"V bucketu {BUCKET} není žádná záloha.")
            name = backups[-1]
        log(f"Obnovuji {BUCKET}/{name}")
        archive = os.path.join(work, "backup.tar.gz")
        oci("os", "object", "get", "--bucket-name", BUCKET, "--name", name, "--file", archive)

    content = os.path.join(work, "content")
    with tarfile.open(archive) as tar:
        tar.extractall(content, filter="data")

    # Snímek PostgreSQL čte restore.sh (pg_restore v kontejneru databáze) – musí být čitelný i pro uživatele deploy.
    for root, dirs, files in os.walk(work):
        for entry in [root, *(os.path.join(root, f) for f in files)]:
            os.chmod(entry, 0o755 if os.path.isdir(entry) else 0o644)
    log("Záloha stažená.")


def restore_files(work: str) -> None:
    """Vymění klíče a obrázky ve volumes za verzi ze zálohy (rozbalené přes fetch); původní nechá vedle."""
    content = os.path.join(work, "content")
    stamp = f"{datetime.now(timezone.utc):%Y%m%dT%H%M%SZ}"
    for destination, target in DIRECTORIES:
        if not os.path.isdir(os.path.join(content, target)):
            log(f"  {target}/: v záloze není, ponechávám současný stav")
            continue
        owner = os.stat(os.path.dirname(destination))  # vlastník volume = uživatel "app" z image
        if os.path.isdir(destination):
            os.replace(destination, f"{destination}.before-restore-{stamp}")
        shutil.copytree(os.path.join(content, target), destination)
        for root, dirs, files in os.walk(destination):
            for entry in [root, *(os.path.join(root, f) for f in files)]:
                os.chown(entry, owner.st_uid, owner.st_gid)
        log(f"  {target}/ → {destination}")

    log("Obnova souborů hotová.")


def main() -> None:
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "backup":
        backup()
    elif command == "fetch" and len(sys.argv) == 4:
        fetch(sys.argv[2], sys.argv[3])
    elif command == "restore-files" and len(sys.argv) == 3:
        restore_files(sys.argv[2])
    elif command == "list":
        for name in list_backups():
            print(name)
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
