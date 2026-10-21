# Síť: totéž, co v kroku 4 vytvořil průvodce VCN, ale jen to, co MiniShop opravdu používá
# (bez privátního subnetu, NAT a Service Gateway).
#
#   internet ── Internet Gateway ── route table (0.0.0.0/0 → IGW) ── veřejný subnet 10.0.0.0/24 ── VM
#                                   security list: dovnitř jen 80, 443 (+ ICMP pro MTU)

resource "oci_core_vcn" "minishop" {
  compartment_id = var.compartment_ocid
  display_name   = "minishop-vcn"
  cidr_blocks    = ["10.0.0.0/16"]
  dns_label      = "minishop" # interní DNS: minishop-vm.public.minishop.oraclevcn.com
}

resource "oci_core_internet_gateway" "minishop" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.minishop.id
  display_name   = "minishop-igw"
  enabled        = true
}

resource "oci_core_route_table" "public" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.minishop.id
  display_name   = "minishop-public-rt"

  route_rules {
    destination       = "0.0.0.0/0"
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_internet_gateway.minishop.id
  }
}

# Cloudový firewall („vrátný u brány“). SSH (22) tu NENÍ – od kroku 8 chodí jen přes Tailscale.
# Nouzově: přidat ingress pravidlo pro 22 z vaší IP (<IP>/32) a tofu apply; pak zase odebrat.
resource "oci_core_security_list" "public" {
  compartment_id = var.compartment_ocid
  vcn_id         = oci_core_vcn.minishop.id
  display_name   = "minishop-public-sl"

  # Ven smí VM kamkoli (apt, Docker Hub, GHCR, GitHub, Let's Encrypt, Tailscale).
  egress_security_rules {
    destination = "0.0.0.0/0"
    protocol    = "all"
  }

  # 80 = Let's Encrypt ověření + přesměrování na https; 443 = MiniShop přes Caddy (krok 5).
  dynamic "ingress_security_rules" {
    for_each = { 80 = "HTTP (Caddy, Let's Encrypt)", 443 = "HTTPS (Caddy)" }
    content {
      source      = "0.0.0.0/0"
      protocol    = "6" # TCP
      description = ingress_security_rules.value
      tcp_options {
        min = ingress_security_rules.key
        max = ingress_security_rules.key
      }
    }
  }

  # ICMP jako ve výchozím security listu OCI: "fragmentation needed" (zjištění MTU) a chyby uvnitř VCN.
  ingress_security_rules {
    source      = "0.0.0.0/0"
    protocol    = "1" # ICMP
    description = "Path MTU discovery"
    icmp_options {
      type = 3
      code = 4
    }
  }

  ingress_security_rules {
    source      = "10.0.0.0/16"
    protocol    = "1"
    description = "ICMP destination unreachable uvnitř VCN"
    icmp_options {
      type = 3
    }
  }
}

resource "oci_core_subnet" "public" {
  compartment_id    = var.compartment_ocid
  vcn_id            = oci_core_vcn.minishop.id
  display_name      = "minishop-public"
  cidr_block        = "10.0.0.0/24"
  dns_label         = "public"
  route_table_id    = oci_core_route_table.public.id
  security_list_ids = [oci_core_security_list.public.id]
}
