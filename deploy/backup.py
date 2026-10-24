"""Cloud krok 12: záloha a obnova dat MiniShopu (SQLite + Data Protection klíče) do/z OCI Object Storage.

Běží v kontejneru ghcr.io/oracle/oci-cli (Python + OCI CLI) – spouští ho backup.sh / restore.sh na VM:
  python3 backup.py backup              → snímek všech DB a klíčů → minishop-RRRRMMDDTHHMMSSZ.tar.gz → bucket
  python3 backup.py restore latest      → nejnovější záloha z bucketu → zpět do volumes (služby musí stát)
  python3 backup.py restore <jméno>     → konkrétní záloha
  python3 backup.py list                → seznam záloh v bucketu

Proč ne obyčejné kopírování souborů: SQLite soubor může být uprostřed zápisu a kopie by byla poškozená.
Online backup API (sqlite3.Connection.backup = příkaz ".backup") udělá konzistentní snímek i za běhu aplikace.

Přihlášení k OCI: instance principal – VM má identitu (dynamická skupina), žádný klíč ani heslo na disku.
Smí do bucketu zapisovat a číst, ale NE mazat (politika v infra/backup.tf); staré zálohy maže lifecycle pravidlo.

Volumes (připojí backup.sh/restore.sh): /web = minishop_web-data, /inventory = minishop_inventory-data.
"""
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone

BUCKET = os.environ.get("BACKUP_BUCKET", "minishop-backups")
PREFIX = "minishop-"

# Co se zálohuje: (cesta ve volume, jméno v archivu). RabbitMQ ne – zprávy jsou dočasné, pravda je v outboxech.
DATABASES = [
    ("/web/ordering.db", "ordering.db"),
    ("/web/journeys.db", "journeys.db"),
    ("/inventory/inventory.db", "inventory.db"),
]
KEYS_DIR = ("/web/keys", "keys")  # Data Protection klíče Webu (cookies, antiforgery)


def log(message: str) -> None:
    print(f"[{datetime.now(timezone.utc):%Y-%m-%dT%H:%M:%SZ}] {message}", flush=True)


def oci(*args: str) -> str:
    """OCI CLI s přihlášením instance principal; vrací stdout (JSON)."""
    result = subprocess.run(["oci", *args, "--auth", "instance_principal"],
                            check=True, capture_output=True, text=True)
    return result.stdout


def check_integrity(path: str) -> None:
    with sqlite3.connect(path) as db:
        status = db.execute("PRAGMA integrity_check").fetchone()[0]
    if status != "ok":
        raise RuntimeError(f"{os.path.basename(path)}: integrity_check = {status}")


def backup() -> None:
    name = f"{PREFIX}{datetime.now(timezone.utc):%Y%m%dT%H%M%SZ}.tar.gz"
    with tempfile.TemporaryDirectory() as work:
        content = os.path.join(work, "content")
        os.makedirs(content)

        for source, target in DATABASES:
            if not os.path.exists(source):
                raise FileNotFoundError(f"Chybí {source} – běží služby? (docker compose ps)")
            destination = os.path.join(content, target)
            # Online backup: konzistentní snímek, i když aplikace právě zapisuje.
            with sqlite3.connect(source) as src, sqlite3.connect(destination) as dst:
                src.backup(dst)
            check_integrity(destination)
            log(f"  {target}: {os.path.getsize(destination) // 1024} kB")

        source, target = KEYS_DIR
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


def restore(name: str) -> None:
    if name != "latest" and not re.fullmatch(r"minishop-\d{8}T\d{6}Z\.tar\.gz", name):
        raise ValueError(f"Čekám 'latest' nebo jméno zálohy (minishop-RRRRMMDDTHHMMSSZ.tar.gz), ne '{name}'.")
    if name == "latest":
        backups = list_backups()
        if not backups:
            raise RuntimeError(f"V bucketu {BUCKET} není žádná záloha.")
        name = backups[-1]
    log(f"Obnovuji {BUCKET}/{name}")

    with tempfile.TemporaryDirectory() as work:
        archive = os.path.join(work, "backup.tar.gz")
        oci("os", "object", "get", "--bucket-name", BUCKET, "--name", name, "--file", archive)
        content = os.path.join(work, "content")
        with tarfile.open(archive) as tar:
            tar.extractall(content, filter="data")

        # Nejdřív zkontrolovat všechno – teprve pak cokoli měnit.
        for _, target in DATABASES:
            check_integrity(os.path.join(content, target))

        stamp = f"{datetime.now(timezone.utc):%Y%m%dT%H%M%SZ}"
        for destination, target in DATABASES:
            owner = os.stat(os.path.dirname(destination))  # vlastník volume = uživatel "app" z image
            if os.path.exists(destination):
                # Původní soubor nechat vedle (pro jistotu), WAL/SHM smazat – patří ke starému souboru.
                os.replace(destination, f"{destination}.before-restore-{stamp}")
            for suffix in ("-wal", "-shm", "-journal"):
                if os.path.exists(destination + suffix):
                    os.remove(destination + suffix)
            shutil.copyfile(os.path.join(content, target), destination)
            os.chown(destination, owner.st_uid, owner.st_gid)
            log(f"  {target} → {destination}")

        destination, target = KEYS_DIR
        if os.path.isdir(os.path.join(content, target)):
            owner = os.stat(os.path.dirname(destination))
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
    elif command == "restore" and len(sys.argv) == 3:
        restore(sys.argv[2])
    elif command == "list":
        for name in list_backups():
            print(name)
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
