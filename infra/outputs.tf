output "public_ip" {
  description = "Reserved IP (na ni ukazuje DNS)."
  value       = oci_core_public_ip.reserved.ip_address
}

output "vm_ocid" {
  description = "OCID VM (pro oci CLI)."
  value       = oci_core_instance.vm.id
}

output "vm_image" {
  description = "Obraz, ze kterého VM vznikla."
  value       = data.oci_core_images.ubuntu.images[0].display_name
}

output "next_steps" {
  description = "Co zbývá po vytvoření nové VM."
  value       = <<-EOT
    1. Počkejte ~5 min na cloud-init, pak v Tailscale adminu: minishop-vm → Edit machine IPv4 → 100.126.167.103
    2. ssh minishop-vm "cloud-init status; sudo tail -n 15 /var/log/minishop-bootstrap.log"
    3. ssh minishop-vm "ssh-keyscan -t ed25519 100.126.167.103 2>/dev/null"  → GitHub proměnná DEPLOY_KNOWN_HOSTS
    4. GitHub → Actions → Deploy → Run workflow (SHA posledního commitu na main s image) → MiniShop se spustí
    5. Data ze zálohy: ssh minishop-vm "sudo -u deploy /opt/minishop/restore.sh latest"
    6. https://${var.domain}
  EOT
}
