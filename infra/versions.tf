# Cloud krok 9: infrastruktura MiniShopu jako kód (OpenTofu).
# Spouští se ze složky infra/ na PC:
#   tofu init "-backend-config=backend.hcl"   # jednou; v PowerShellu v uvozovkách (stáhne provider, připojí state v OCI)
#   tofu plan                                 # co by se změnilo
#   tofu apply                                # provede to
# Návod: docs/kroky-cloud/krok-09-iac.md

terraform {
  required_version = ">= 1.10"

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 9.10"
    }
  }

  # State (= co už v cloudu existuje a pod jakým OCID) leží v bucketu OCI Object Storage přes jeho
  # S3-kompatibilní API. Adresu a cestu k přístupovému klíči doplní backend.hcl (vzor backend.hcl.example),
  # protože obsahují namespace tenancy a cestu na vašem PC.
  backend "s3" {
    bucket = "minishop-tfstate"
    key    = "minishop/terraform.tfstate"
    region = "eu-frankfurt-1"

    # OCI není AWS – vypneme kontroly, které by se ptaly AWS služeb (účet, regiony, metadata).
    skip_region_validation      = true
    skip_credentials_validation = true
    skip_requesting_account_id  = true
    skip_metadata_api_check     = true
    # OCI nepodporuje kontrolní součty, které novější AWS SDK posílá v hlavičkách.
    skip_s3_checksum = true
    use_path_style   = true
  }
}

# Přihlášení technickým uživatelem minishop-automation z kroku 3 (~/.oci/config + API klíč).
# Ten smí jen do compartmentu minishop – OpenTofu nic jiného změnit nemůže, ani omylem.
provider "oci" {
  config_file_profile = var.oci_config_profile
  region              = var.region
}
