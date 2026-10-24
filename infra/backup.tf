# Cloud krok 12: úložiště záloh MiniShopu MIMO VM (OCI Object Storage, Always Free 20 GB).
#
#   VM (instance principal = dynamická skupina minishop-vm) ──zapsat/číst, NE mazat──► bucket minishop-backups
#                                                                                       └─ lifecycle: smazat po 7 dnech
#
# Dynamickou skupinu (identita VM) zakládá uživatel ručně v konzoli – je na úrovni tenancy, kam OpenTofu
# (uživatel minishop-automation) nesmí. Viz docs/kroky-cloud/krok-12-zalohy.md, bod B.

data "oci_objectstorage_namespace" "this" {
  compartment_id = var.compartment_ocid
}

resource "oci_objectstorage_bucket" "backups" {
  compartment_id = var.compartment_ocid
  namespace      = data.oci_objectstorage_namespace.this.namespace
  name           = "minishop-backups"
  access_type    = "NoPublicAccess"
  storage_tier   = "Standard"
  # Verzování ne: každá záloha má vlastní jméno (čas), přepisovat se nic nemá.
  versioning = "Disabled"

  lifecycle {
    # Zálohy jsou to jediné, co přežije havárii VM – bucket se nesmí smazat omylem (ani tofu destroy).
    prevent_destroy = true
  }
}

# Staré zálohy maže OCI samo. Proto VM mazat nemusí (a nesmí – viz politika níže).
resource "oci_objectstorage_object_lifecycle_policy" "backups" {
  namespace = data.oci_objectstorage_namespace.this.namespace
  bucket    = oci_objectstorage_bucket.backups.name

  rules {
    name        = "smazat-zalohy-po-${var.backup_retention_days}-dnech"
    action      = "DELETE"
    target      = "objects"
    time_amount = var.backup_retention_days
    time_unit   = "DAYS"
    is_enabled  = true
  }

  # Lifecycle pravidla provádí služba Object Storage – musí k tomu mít oprávnění (politika níže).
  depends_on = [oci_identity_policy.backups]
}

resource "oci_identity_policy" "backups" {
  compartment_id = var.compartment_ocid
  name           = "minishop-backups"
  description    = "Zálohy MiniShopu: VM zapisuje/čte bucket minishop-backups (nemaže), Object Storage maže staré zálohy"

  statements = [
    # Služba Object Storage ve Frankfurtu smí provádět lifecycle pravidla (mazat staré objekty).
    "Allow service objectstorage-${var.region} to manage object-family in compartment id ${var.compartment_ocid}",
    # VM (instance principal): najít bucket, číst a vypsat zálohy (restore.sh)…
    "Allow dynamic-group '${var.identity_domain}'/'${var.vm_dynamic_group}' to read buckets in compartment id ${var.compartment_ocid} where target.bucket.name = '${oci_objectstorage_bucket.backups.name}'",
    "Allow dynamic-group '${var.identity_domain}'/'${var.vm_dynamic_group}' to read objects in compartment id ${var.compartment_ocid} where target.bucket.name = '${oci_objectstorage_bucket.backups.name}'",
    # …a nahrávat NOVÉ zálohy (backup.sh). Bez OBJECT_OVERWRITE a OBJECT_DELETE – napadená VM zálohy nezničí.
    "Allow dynamic-group '${var.identity_domain}'/'${var.vm_dynamic_group}' to manage objects in compartment id ${var.compartment_ocid} where all {target.bucket.name = '${oci_objectstorage_bucket.backups.name}', request.permission = 'OBJECT_CREATE'}",
  ]
}
