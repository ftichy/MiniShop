# Cloud krok 11: alarmy OCI Monitoring → e-mail přes OCI Notifications (vše v Always Free).
# Metriky VM (CPU, paměť) posílá Oracle Cloud Agent, předinstalovaný v obrazech Ubuntu od Oracle (namespace oci_computeagent).
# Doplňuje UptimeRobot: ten hlídá aplikaci zvenku, tyhle alarmy stav VM zevnitř cloudu.
#
#   VM ──metriky každou minutu──► OCI Monitoring ──alarm (dotaz MQL)──► téma minishop-alerts ──► e-mail
#
# E-mail po prvním apply přijde s odkazem "Confirm subscription" – bez potvrzení se nic neposílá.

resource "oci_ons_notification_topic" "alerts" {
  compartment_id = var.compartment_ocid
  name           = "minishop-alerts"
  description    = "Upozornění MiniShopu (alarmy VM)"
}

resource "oci_ons_subscription" "email" {
  compartment_id = var.compartment_ocid
  topic_id       = oci_ons_notification_topic.alerts.id
  protocol       = "EMAIL"
  endpoint       = var.alert_email
}

locals {
  # Dotazy MQL: metrika[okno]{filtr}.statistika() podmínka. Filtr = jen naše VM.
  vm_filter = "{resourceId = \"${oci_core_instance.vm.id}\"}"

  alarms = {
    cpu = {
      name     = "minishop-vm-cpu"
      query    = "CpuUtilization[5m]${local.vm_filter}.mean() > 80"
      severity = "WARNING"
      body     = "CPU VM minishop-vm je přes 15 minut nad 80 %. Zkontrolujte: ssh minishop-vm \"docker stats --no-stream\"."
    }
    memory = {
      name     = "minishop-vm-memory"
      query    = "MemoryUtilization[5m]${local.vm_filter}.mean() > 90"
      severity = "WARNING"
      body     = "Paměť VM minishop-vm je přes 15 minut nad 90 %. Zkontrolujte: ssh minishop-vm \"docker stats --no-stream\"."
    }
    # absent() = metrika přestala chodit → VM neběží nebo zamrzla (případně spadl agent).
    down = {
      name     = "minishop-vm-no-metrics"
      query    = "CpuUtilization[5m]${local.vm_filter}.absent()"
      severity = "CRITICAL"
      body     = "VM minishop-vm 15 minut neposílá metriky – nejspíš neběží. OCI konzole → Compute → Instances."
    }
  }
}

resource "oci_monitoring_alarm" "vm" {
  for_each = local.alarms

  compartment_id        = var.compartment_ocid
  metric_compartment_id = var.compartment_ocid
  display_name          = each.value.name
  namespace             = "oci_computeagent"
  query                 = each.value.query
  severity              = each.value.severity
  body                  = each.value.body
  is_enabled            = true

  # Podmínka musí platit 15 minut v kuse – krátké špičky (build, restart) alarm nespustí.
  pending_duration = "PT15M"
  # Dokud problém trvá, připomenout každé 4 hodiny (ne každou minutu).
  repeat_notification_duration = "PT4H"
  message_format               = "ONS_OPTIMIZED"
  destinations                 = [oci_ons_notification_topic.alerts.id]
}
