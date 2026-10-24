# VM a její veřejná adresa.

data "oci_identity_availability_domains" "all" {
  compartment_id = var.compartment_ocid
}

# Nejnovější Ubuntu 24.04 pro ARM (ne "Minimal" – to nemá netfilter-persistent a další balíčky z kroku 4).
data "oci_core_images" "ubuntu" {
  compartment_id           = var.compartment_ocid
  operating_system         = "Canonical Ubuntu"
  operating_system_version = "24.04"
  shape                    = local.shape
  sort_by                  = "TIMECREATED"
  sort_order               = "DESC"

  filter {
    name   = "display_name"
    values = ["^Canonical-Ubuntu-24\\.04-aarch64-"]
    regex  = true
  }
}

locals {
  shape = "VM.Standard.A1.Flex"

  # cloud-init = co VM provede při prvním startu (Docker, firewall, Tailscale, uživatel deploy).
  # Skripty se vloží jako base64, aby jejich obsah nerozbilo odsazení YAML.
  # Krok 10: žádné secrets aplikace – ty na VM pošle až workflow Deploy z GitHubu.
  user_data = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    deploy_script_b64  = filebase64("${path.module}/../deploy/deploy.sh")
    backup_service_b64 = filebase64("${path.module}/../deploy/minishop-backup.service")
    backup_timer_b64   = filebase64("${path.module}/../deploy/minishop-backup.timer")
    bootstrap_b64 = base64encode(templatefile("${path.module}/bootstrap.sh.tftpl", {
      tailscale_auth_key    = var.tailscale_auth_key
      deploy_ssh_public_key = trimspace(file(pathexpand(var.deploy_ssh_public_key_path)))
    }))
  })
}

resource "oci_core_instance" "vm" {
  compartment_id      = var.compartment_ocid
  availability_domain = data.oci_identity_availability_domains.all.availability_domains[var.availability_domain_index].name
  display_name        = "minishop-vm"
  shape               = local.shape

  shape_config {
    ocpus         = var.vm_ocpus
    memory_in_gbs = var.vm_memory_gbs
  }

  source_details {
    source_type = "image"
    source_id   = data.oci_core_images.ubuntu.images[0].id
  }

  create_vnic_details {
    subnet_id      = oci_core_subnet.public.id
    hostname_label = "minishop-vm"
    # Žádná dočasná (ephemeral) IP – VM dostane reserved IP níže, ta přežije i nové vytvoření VM.
    assign_public_ip = false
  }

  metadata = {
    ssh_authorized_keys = trimspace(file(pathexpand(var.ssh_public_key_path)))
    user_data           = base64encode(local.user_data)
  }

  # Smazání VM smaže i její disk (data jsou zatím jen na něm – zálohy řeší krok 12).
  preserve_boot_volume = false

  lifecycle {
    # Nový obraz Ubuntu ani změna cloud-init NESMÍ sama vést k novému vytvoření VM (= ztráta dat).
    # cloud-init běží jen při prvním startu; novou VM vynutíte vědomě: tofu apply -replace=oci_core_instance.vm
    ignore_changes = [source_details[0].source_id, metadata]
  }
}

# Primární privátní IP VM (10.0.0.x) – na ni se "přišpendlí" reserved IP.
data "oci_core_vnic_attachments" "vm" {
  compartment_id = var.compartment_ocid
  instance_id    = oci_core_instance.vm.id
}

data "oci_core_private_ips" "vm" {
  vnic_id = data.oci_core_vnic_attachments.vm.vnic_attachments[0].vnic_id
}

# Reserved IP z kroku 5 – na ni ukazuje DNS (minishop-tichy.duckdns.org). Vznikla ručně, do OpenTofu se
# jednou převezme příkazem tofu import (viz návod). Nikdy se nemaže: nová IP = změna DNS a nový certifikát.
resource "oci_core_public_ip" "reserved" {
  compartment_id = var.compartment_ocid
  display_name   = "minishop-ip"
  lifetime       = "RESERVED"
  private_ip_id  = data.oci_core_private_ips.vm.private_ips[0].id

  lifecycle {
    prevent_destroy = true
  }
}
