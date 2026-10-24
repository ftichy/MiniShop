# Vstupy. Hodnoty bez výchozí hodnoty patří do terraform.tfvars (vzor terraform.tfvars.example, soubor je v .gitignore).

variable "region" {
  description = "Region OCI (Always Free A1 máme ve Frankfurtu)."
  type        = string
  default     = "eu-frankfurt-1"
}

variable "oci_config_profile" {
  description = "Profil v ~/.oci/config (technický uživatel minishop-automation)."
  type        = string
  default     = "DEFAULT"
}

variable "compartment_ocid" {
  description = "OCID compartmentu minishop (krok 3)."
  type        = string
}

variable "availability_domain_index" {
  description = "Která availability domain (0 = AD-1). Při chybě 'Out of host capacity' zkuste 1 nebo 2."
  type        = number
  default     = 0
}

variable "vm_ocpus" {
  description = "Počet jader A1 (Always Free: celkem max. 4; kvóta compartmentu: 2)."
  type        = number
  default     = 2
}

variable "vm_memory_gbs" {
  description = "Paměť VM v GB (Always Free: celkem max. 24; kvóta compartmentu: 12)."
  type        = number
  default     = 12
}

variable "ssh_public_key_path" {
  description = "Veřejný SSH klíč pro uživatele ubuntu (krok 4) – nouzový a správcovský přístup."
  type        = string
  default     = "~/.ssh/minishop_oci.pub"
}

variable "deploy_ssh_public_key_path" {
  description = "Veřejný klíč GitHub Actions pro uživatele deploy (krok 8; soukromá část je v secretu DEPLOY_SSH_KEY)."
  type        = string
  default     = "~/.ssh/minishop_deploy.pub"
}

variable "tailscale_auth_key" {
  description = "Jednorázový Tailscale auth key s tagem tag:minishop-vm (tskey-auth-…). Po prvním použití je bezcenný."
  type        = string
  sensitive   = true
}

variable "alert_email" {
  description = "E-mail pro alarmy VM (krok 11). Po prvním apply přijde výzva k potvrzení odběru."
  type        = string

  validation {
    condition     = can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.alert_email))
    error_message = "Čekám e-mailovou adresu."
  }
}

variable "domain" {
  description = "Doména MiniShopu – jen pro výpis dalších kroků (Caddy ji bere z deploy/production.env)."
  type        = string
  default     = "minishop-tichy.duckdns.org"
}
