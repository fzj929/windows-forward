<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Connection, Delete, Edit, Refresh, Search, SwitchButton, View } from '@element-plus/icons-vue'
import {
  createRule,
  deleteRule,
  disableRule,
  enableRule,
  ForwardProtocol,
  ForwardRuleType,
  listCommandLogs,
  listRules,
  listSystemFirewallRules,
  listSystemNatMappings,
  listSystemPortProxyRules,
  listSystemRoutes,
  updateRule,
  validateRule,
  type CommandExecutionLog,
  type FieldError,
  type ForwardRule,
  type ForwardRuleInput,
  type SystemFirewallRule,
  type SystemNatStaticMapping,
  type SystemRouteRule,
  type SystemPortProxyRule
} from './api/rules'

type MenuKey = ForwardRuleType | 'logs'
type RuleStatus = 'all' | 'enabled' | 'disabled'

interface DisplayRuleRow {
  key: string
  source: 'database' | 'system'
  name: string
  type: ForwardRuleType
  detail: string
  actualEnabled: boolean
  updatedAt?: string
  rule?: ForwardRule
  systemRule?: SystemPortProxyRule
  systemFirewallRule?: SystemFirewallRule
  systemNatMapping?: SystemNatStaticMapping
  systemRouteRule?: SystemRouteRule
}

const typeOptions = [
  { label: '端口转发', value: ForwardRuleType.PortProxy, hint: 'portproxy 规则会和操作系统当前配置比对，显示真实启用状态。' },
  { label: '防火墙放行', value: ForwardRuleType.Firewall, hint: '创建或删除 Windows 入站防火墙规则。' },
  { label: 'NAT 静态映射', value: ForwardRuleType.Nat, hint: '通过 NetNat 维护外部端口到内部地址的映射。' },
  { label: '静态路由', value: ForwardRuleType.Route, hint: '维护 Windows 永久路由。' },
  { label: 'IP 转发开关', value: ForwardRuleType.IpForwarding, hint: '维护 Windows IPEnableRouter 开关。' },
  { label: 'SSH 本地转发', value: ForwardRuleType.SshLocal, hint: 'ssh -L，本机端口转发到 SSH 侧可访问的目标。' },
  { label: 'SSH 远程转发', value: ForwardRuleType.SshRemote, hint: 'ssh -R，远端端口转发到本机可访问的目标。' },
  { label: 'SSH 动态 SOCKS', value: ForwardRuleType.SshDynamic, hint: 'ssh -D，启动 SOCKS 代理。' }
]

const protocolOptions = [
  { label: 'TCP', value: ForwardProtocol.Tcp },
  { label: 'UDP', value: ForwardProtocol.Udp }
]

const selectedMenu = ref<MenuKey>(ForwardRuleType.PortProxy)
const rules = ref<ForwardRule[]>([])
const systemPortProxyRules = ref<SystemPortProxyRule[]>([])
const systemFirewallRules = ref<SystemFirewallRule[]>([])
const systemNatMappings = ref<SystemNatStaticMapping[]>([])
const systemRouteRules = ref<SystemRouteRule[]>([])
const commandLogs = ref<CommandExecutionLog[]>([])
const loading = ref(false)
const systemLoading = ref(false)
const logsLoading = ref(false)
const saving = ref(false)
const applyingKey = ref<string>()
const preview = ref('')
const formRef = ref<FormInstance>()
const editingId = ref<number>()
const ruleQuery = ref('')
const ruleStatus = ref<RuleStatus>('all')

const form = reactive<ForwardRuleInput>({
  name: '',
  description: '',
  type: ForwardRuleType.PortProxy,
  protocol: ForwardProtocol.Tcp,
  listenAddress: '0.0.0.0',
  listenPort: 8080,
  connectAddress: '127.0.0.1',
  connectPort: 80,
  natName: 'MyNAT',
  prefix: '',
  routeDestination: '',
  routeMask: '255.255.255.0',
  routeGateway: '',
  sshHost: '',
  sshUser: ''
})

const ruleset = computed<FormRules<ForwardRuleInput>>(() => ({
  name: [{ required: true, message: '请填写规则名称。', trigger: 'blur' }],
  listenPort: [{ type: 'number', min: 1, max: 65535, message: '端口必须在 1-65535 之间。', trigger: 'blur' }],
  connectPort: [{ type: 'number', min: 1, max: 65535, message: '端口必须在 1-65535 之间。', trigger: 'blur' }]
}))

const selectedType = computed(() => (selectedMenu.value === 'logs' ? undefined : selectedMenu.value))
const activeType = computed(() => typeOptions.find(x => x.value === (selectedType.value ?? form.type)) ?? typeOptions[0])
const isPortLike = computed(() => [ForwardRuleType.PortProxy, ForwardRuleType.Firewall, ForwardRuleType.Nat, ForwardRuleType.SshLocal, ForwardRuleType.SshRemote, ForwardRuleType.SshDynamic].includes(form.type))
const needsTarget = computed(() => [ForwardRuleType.PortProxy, ForwardRuleType.Nat, ForwardRuleType.SshLocal, ForwardRuleType.SshRemote].includes(form.type))
const needsSsh = computed(() => [ForwardRuleType.SshLocal, ForwardRuleType.SshRemote, ForwardRuleType.SshDynamic].includes(form.type))
const needsRoute = computed(() => form.type === ForwardRuleType.Route)
const enabledCount = computed(() => displayRows.value.filter(x => x.actualEnabled).length)
const disabledCount = computed(() => displayRows.value.filter(x => !x.actualEnabled).length)
const failedLogCount = computed(() => commandLogs.value.filter(x => !x.success).length)
const latestLog = computed(() => commandLogs.value[0])

const menuItems = computed(() => [
  ...typeOptions.map(item => ({
    key: item.value as MenuKey,
    label: item.label,
    count: rules.value.filter(rule => rule.type === item.value).length
  })),
  { key: 'logs' as MenuKey, label: '执行日志', count: commandLogs.value.length }
])

const displayRows = computed<DisplayRuleRow[]>(() => {
  if (selectedType.value === undefined) return []

  const databaseRows = rules.value
    .filter(rule => rule.type === selectedType.value)
    .map(rule => ({
      key: `db-${rule.id}`,
      source: 'database' as const,
      name: rule.name,
      type: rule.type,
      detail: describe(rule),
      actualEnabled: isSystemEnabled(rule),
      updatedAt: rule.updatedAt,
      rule
    }))

  const databaseKeys = new Set(databaseRows.map(row => systemKeyFromInput(row.rule!)).filter(Boolean))
  const systemOnlyRows = systemRowsForType(selectedType.value, databaseKeys)

  return [...databaseRows, ...systemOnlyRows]
})

const visibleRows = computed(() => {
  const keyword = ruleQuery.value.trim().toLowerCase()
  return displayRows.value.filter(row => {
    const statusMatched =
      ruleStatus.value === 'all' ||
      (ruleStatus.value === 'enabled' && row.actualEnabled) ||
      (ruleStatus.value === 'disabled' && !row.actualEnabled)
    const keywordMatched =
      !keyword ||
      row.name.toLowerCase().includes(keyword) ||
      row.detail.toLowerCase().includes(keyword) ||
      typeName(row.type).toLowerCase().includes(keyword)
    return statusMatched && keywordMatched
  })
})

function selectMenu(key: MenuKey) {
  selectedMenu.value = key
  ruleQuery.value = ''
  ruleStatus.value = 'all'
  if (key !== 'logs' && !editingId.value) {
    form.type = key
  }
}

function typeName(type: ForwardRuleType) {
  return typeOptions.find(x => x.value === type)?.label ?? '未知'
}

function protocolName(protocol: ForwardProtocol) {
  return protocol === ForwardProtocol.Udp ? 'UDP' : 'TCP'
}

function protocolFromName(protocol: string) {
  return protocol.toUpperCase() === 'UDP' ? ForwardProtocol.Udp : ForwardProtocol.Tcp
}

function describe(rule: ForwardRule) {
  if (rule.type === ForwardRuleType.Route) return `${rule.routeDestination}/${rule.routeMask} -> ${rule.routeGateway}`
  if (rule.type === ForwardRuleType.IpForwarding) return 'Windows IP 转发总开关'
  if (rule.type === ForwardRuleType.SshDynamic) return `SOCKS :${rule.listenPort} -> ${rule.sshHost}`
  if (rule.type === ForwardRuleType.Firewall) return `${protocolName(rule.protocol)} :${rule.listenPort}`
  return `${rule.listenAddress ?? '0.0.0.0'}:${rule.listenPort} -> ${rule.connectAddress}:${rule.connectPort}`
}

function cloneForm(rule: ForwardRule): ForwardRuleInput {
  return {
    name: rule.name,
    description: rule.description,
    type: rule.type,
    protocol: rule.protocol,
    listenAddress: rule.listenAddress,
    listenPort: rule.listenPort,
    connectAddress: rule.connectAddress,
    connectPort: rule.connectPort,
    natName: rule.natName,
    prefix: rule.prefix,
    routeDestination: rule.routeDestination,
    routeMask: rule.routeMask,
    routeGateway: rule.routeGateway,
    sshHost: rule.sshHost,
    sshUser: rule.sshUser
  }
}

function resetForm() {
  editingId.value = undefined
  Object.assign(form, {
    name: '',
    description: '',
    type: selectedType.value ?? ForwardRuleType.PortProxy,
    protocol: ForwardProtocol.Tcp,
    listenAddress: '0.0.0.0',
    listenPort: 8080,
    connectAddress: '127.0.0.1',
    connectPort: 80,
    natName: 'MyNAT',
    prefix: '',
    routeDestination: '',
    routeMask: '255.255.255.0',
    routeGateway: '',
    sshHost: '',
    sshUser: ''
  })
  preview.value = ''
  formRef.value?.clearValidate()
}

function routePrefixFromInput(rule: Pick<ForwardRule, 'routeDestination' | 'routeMask'>) {
  const prefixLength = maskToPrefixLength(rule.routeMask)
  return rule.routeDestination && prefixLength !== undefined ? `${rule.routeDestination}/${prefixLength}` : ''
}

function maskToPrefixLength(mask?: string) {
  if (!mask) return undefined
  const octets = mask.split('.').map(Number)
  if (octets.length !== 4 || octets.some(x => !Number.isInteger(x) || x < 0 || x > 255)) return undefined
  const binary = octets.map(x => x.toString(2).padStart(8, '0')).join('')
  if (!/^1*0*$/.test(binary)) return undefined
  return binary.indexOf('0') === -1 ? 32 : binary.indexOf('0')
}

function cidrToMask(prefixLength: number) {
  const length = Number.isInteger(prefixLength) && prefixLength >= 0 && prefixLength <= 32 ? prefixLength : 24
  const bits = '1'.repeat(length).padEnd(32, '0')
  return bits.match(/.{8}/g)!.map(x => Number.parseInt(x, 2)).join('.')
}

function portProxyKeyFromInput(rule: Pick<ForwardRule, 'listenAddress' | 'listenPort' | 'connectAddress' | 'connectPort'>) {
  return `${rule.listenAddress ?? '0.0.0.0'}:${rule.listenPort}->${rule.connectAddress}:${rule.connectPort}`.toLowerCase()
}

function portProxyKeyFromSystem(rule: SystemPortProxyRule) {
  return `${rule.listenAddress}:${rule.listenPort}->${rule.connectAddress}:${rule.connectPort}`.toLowerCase()
}

function firewallKeyFromInput(rule: Pick<ForwardRule, 'protocol' | 'listenPort'>) {
  return `firewall:${protocolName(rule.protocol)}:${rule.listenPort}`.toLowerCase()
}

function firewallKeyFromSystem(rule: SystemFirewallRule) {
  const port = Number(rule.localPort)
  if (!Number.isInteger(port) || port < 1 || port > 65535) return ''
  return `firewall:${rule.protocol}:${port}`.toLowerCase()
}

function natKeyFromInput(rule: Pick<ForwardRule, 'natName' | 'protocol' | 'listenPort' | 'connectAddress' | 'connectPort'>) {
  return `nat:${rule.natName}:${protocolName(rule.protocol)}:${rule.listenPort}->${rule.connectAddress}:${rule.connectPort}`.toLowerCase()
}

function natKeyFromSystem(rule: SystemNatStaticMapping) {
  return `nat:${rule.natName}:${rule.protocol}:${rule.externalPort}->${rule.internalIPAddress}:${rule.internalPort}`.toLowerCase()
}

function routeKeyFromInput(rule: Pick<ForwardRule, 'routeDestination' | 'routeMask' | 'routeGateway'>) {
  const prefix = routePrefixFromInput(rule)
  if (!prefix || !rule.routeGateway) return ''
  return `route:${prefix}->${rule.routeGateway}`.toLowerCase()
}

function routeKeyFromSystem(rule: SystemRouteRule) {
  return `route:${rule.destinationPrefix}->${rule.nextHop}`.toLowerCase()
}

function systemKeyFromInput(rule: ForwardRule) {
  if (rule.type === ForwardRuleType.PortProxy) return portProxyKeyFromInput(rule)
  if (rule.type === ForwardRuleType.Firewall) return firewallKeyFromInput(rule)
  if (rule.type === ForwardRuleType.Nat) return natKeyFromInput(rule)
  if (rule.type === ForwardRuleType.Route) return routeKeyFromInput(rule)
  return ''
}

function systemRowsForType(type: ForwardRuleType, databaseKeys: Set<string>): DisplayRuleRow[] {
  if (type === ForwardRuleType.PortProxy) {
    return systemPortProxyRules.value
      .filter(rule => !databaseKeys.has(portProxyKeyFromSystem(rule)))
      .map(rule => ({
        key: `sys-${portProxyKeyFromSystem(rule)}`,
        source: 'system' as const,
        name: `系统规则 ${rule.listenPort}`,
        type: ForwardRuleType.PortProxy,
        detail: `${rule.listenAddress}:${rule.listenPort} -> ${rule.connectAddress}:${rule.connectPort}`,
        actualEnabled: true,
        systemRule: rule
      }))
  }

  if (type === ForwardRuleType.Firewall) {
    return systemFirewallRules.value
      .filter(rule => firewallKeyFromSystem(rule) && !databaseKeys.has(firewallKeyFromSystem(rule)))
      .map(rule => ({
        key: `sys-${firewallKeyFromSystem(rule)}-${rule.displayName}`,
        source: 'system' as const,
        name: rule.displayName,
        type: ForwardRuleType.Firewall,
        detail: `${rule.protocol} :${rule.localPort} / ${rule.enabled === 'True' ? '系统已启用' : '系统已禁用'}`,
        actualEnabled: rule.enabled === 'True',
        systemFirewallRule: rule
      }))
  }

  if (type === ForwardRuleType.Nat) {
    return systemNatMappings.value
      .filter(rule => !databaseKeys.has(natKeyFromSystem(rule)))
      .map(rule => ({
        key: `sys-${natKeyFromSystem(rule)}`,
        source: 'system' as const,
        name: `${rule.natName} ${rule.externalPort}`,
        type: ForwardRuleType.Nat,
        detail: `${rule.protocol} ${rule.externalIPAddress}:${rule.externalPort} -> ${rule.internalIPAddress}:${rule.internalPort}`,
        actualEnabled: true,
        systemNatMapping: rule
      }))
  }

  if (type === ForwardRuleType.Route) {
    return systemRouteRules.value
      .filter(rule => !databaseKeys.has(routeKeyFromSystem(rule)))
      .map(rule => ({
        key: `sys-${routeKeyFromSystem(rule)}-${rule.interfaceAlias}-${rule.routeMetric}`,
        source: 'system' as const,
        name: `${rule.destinationPrefix} -> ${rule.nextHop}`,
        type: ForwardRuleType.Route,
        detail: `${rule.destinationPrefix} -> ${rule.nextHop} / ${rule.interfaceAlias || '-'} / metric ${rule.routeMetric}`,
        actualEnabled: true,
        systemRouteRule: rule
      }))
  }

  return []
}

function isSystemEnabled(rule: ForwardRule) {
  if (rule.type === ForwardRuleType.PortProxy) {
    const key = portProxyKeyFromInput(rule)
    return systemPortProxyRules.value.some(systemRule => portProxyKeyFromSystem(systemRule) === key)
  }
  if (rule.type === ForwardRuleType.Firewall) {
    const key = firewallKeyFromInput(rule)
    return systemFirewallRules.value.some(systemRule => systemRule.enabled === 'True' && firewallKeyFromSystem(systemRule) === key)
  }
  if (rule.type === ForwardRuleType.Nat) {
    const key = natKeyFromInput(rule)
    return systemNatMappings.value.some(systemRule => natKeyFromSystem(systemRule) === key)
  }
  if (rule.type === ForwardRuleType.Route) {
    const key = routeKeyFromInput(rule)
    return systemRouteRules.value.some(systemRule => routeKeyFromSystem(systemRule) === key)
  }
  return rule.enabled
}

function inputFromSystemRow(row: DisplayRuleRow): ForwardRuleInput | undefined {
  if (row.systemRule) {
    return {
      name: `系统导入 ${row.systemRule.listenPort}`,
      description: '从操作系统 portproxy 配置导入。',
      type: ForwardRuleType.PortProxy,
      protocol: ForwardProtocol.Tcp,
      listenAddress: row.systemRule.listenAddress,
      listenPort: row.systemRule.listenPort,
      connectAddress: row.systemRule.connectAddress,
      connectPort: row.systemRule.connectPort
    }
  }
  if (row.systemFirewallRule) {
    return {
      name: row.systemFirewallRule.displayName.replace(/^Windows Forward - /, ''),
      description: '从操作系统防火墙放行规则导入。',
      type: ForwardRuleType.Firewall,
      protocol: protocolFromName(row.systemFirewallRule.protocol),
      listenPort: Number(row.systemFirewallRule.localPort)
    }
  }
  if (row.systemNatMapping) {
    return {
      name: `${row.systemNatMapping.natName} ${row.systemNatMapping.externalPort}`,
      description: '从操作系统 NAT 静态映射导入。',
      type: ForwardRuleType.Nat,
      protocol: protocolFromName(row.systemNatMapping.protocol),
      listenPort: row.systemNatMapping.externalPort,
      connectAddress: row.systemNatMapping.internalIPAddress,
      connectPort: row.systemNatMapping.internalPort,
      natName: row.systemNatMapping.natName
    }
  }
  if (row.systemRouteRule) {
    const [destination, prefixLength] = row.systemRouteRule.destinationPrefix.split('/')
    return {
      name: `${row.systemRouteRule.destinationPrefix} -> ${row.systemRouteRule.nextHop}`,
      description: '从操作系统静态路由导入。',
      type: ForwardRuleType.Route,
      protocol: ForwardProtocol.Tcp,
      routeDestination: destination,
      routeMask: cidrToMask(Number(prefixLength)),
      routeGateway: row.systemRouteRule.nextHop
    }
  }
  return undefined
}

function surfaceError(error: unknown) {
  const err = error as { response?: { data?: { message?: string; data?: FieldError[] | string } } }
  const payload = err.response?.data
  if (Array.isArray(payload?.data)) {
    ElMessage.error(payload.data.map(x => x.message).join('；'))
    return
  }
  ElMessage.error(payload?.message ?? '操作失败，请检查配置和服务权限。')
}

async function refreshRules() {
  loading.value = true
  try {
    rules.value = await listRules()
  } finally {
    loading.value = false
  }
}

async function refreshSystemRules() {
  systemLoading.value = true
  try {
    const [portProxyRules, firewallRules, natMappings, routeRules] = await Promise.all([
      listSystemPortProxyRules(),
      listSystemFirewallRules(),
      listSystemNatMappings(),
      listSystemRoutes()
    ])
    systemPortProxyRules.value = portProxyRules
    systemFirewallRules.value = firewallRules
    systemNatMappings.value = natMappings
    systemRouteRules.value = routeRules
  } finally {
    systemLoading.value = false
  }
}

async function refreshLogs() {
  logsLoading.value = true
  try {
    commandLogs.value = await listCommandLogs(50)
  } finally {
    logsLoading.value = false
  }
}

async function refreshDashboard() {
  await Promise.all([refreshRules(), refreshSystemRules(), refreshLogs()])
}

async function previewCommand() {
  try {
    const result = await validateRule({ ...form })
    preview.value = result.data ?? ''
    ElMessage.success(result.message)
  } catch (error) {
    preview.value = ''
    surfaceError(error)
  }
}

async function save() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    if (editingId.value) {
      await updateRule(editingId.value, { ...form })
      ElMessage.success('规则已更新。')
    } else {
      await createRule({ ...form })
      ElMessage.success('规则已保存，默认未启用。')
    }
    resetForm()
    await refreshDashboard()
  } catch (error) {
    surfaceError(error)
  } finally {
    saving.value = false
  }
}

function edit(row: DisplayRuleRow) {
  if (!row.rule) return
  if (row.actualEnabled) {
    ElMessage.warning('请先禁用系统中的规则，再修改配置。')
    return
  }
  editingId.value = row.rule.id
  selectedMenu.value = row.rule.type
  Object.assign(form, cloneForm(row.rule))
  preview.value = ''
}

async function importSystemRule(row: DisplayRuleRow) {
  const input = inputFromSystemRow(row)
  if (!input) return
  applyingKey.value = row.key
  try {
    await createRule(input)
    ElMessage.success('系统规则已保存到数据库。')
    await refreshDashboard()
  } catch (error) {
    surfaceError(error)
  } finally {
    applyingKey.value = undefined
  }
}

async function disableImportedSystemRule(row: DisplayRuleRow) {
  const input = inputFromSystemRow(row)
  if (!input) return
  applyingKey.value = row.key
  try {
    const created = await createRule(input)
    await disableRule(created.id)
    ElMessage.success('系统规则已入库并禁用。')
    await refreshDashboard()
  } catch (error) {
    surfaceError(error)
  } finally {
    applyingKey.value = undefined
  }
}

async function toggle(row: DisplayRuleRow) {
  if (!row.rule) return
  applyingKey.value = row.key
  try {
    const result = row.actualEnabled ? await disableRule(row.rule.id) : await enableRule(row.rule.id)
    ElMessage.success(result.message)
    await refreshDashboard()
  } catch (error) {
    surfaceError(error)
  } finally {
    applyingKey.value = undefined
  }
}

async function remove(row: DisplayRuleRow) {
  if (!row.rule) return
  if (row.actualEnabled) {
    ElMessage.warning('请先禁用系统中的规则，再删除数据库配置。')
    return
  }
  await ElMessageBox.confirm(`确认删除规则“${row.rule.name}”？`, '删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消'
  })
  try {
    const result = await deleteRule(row.rule.id)
    ElMessage.success(result.message)
    await refreshDashboard()
  } catch (error) {
    surfaceError(error)
  }
}

onMounted(refreshDashboard)
</script>

<template>
  <main class="app-shell">
    <aside class="side-nav">
      <div class="brand">
        <img class="app-logo" src="/windows-forward.svg" alt="Windows Forward" />
        <div>
          <p>WINDOWS</p>
          <strong>Forward</strong>
        </div>
      </div>

      <div class="menu-section">
        <span>规则类型</span>
        <button
          v-for="item in menuItems.filter(x => x.key !== 'logs')"
          :key="item.key"
          class="menu-item"
          :class="{ active: selectedMenu === item.key }"
          @click="selectMenu(item.key)"
        >
          <span>{{ item.label }}</span>
          <b>{{ item.count }}</b>
        </button>
      </div>

      <div class="menu-section">
        <span>审计</span>
        <button class="menu-item" :class="{ active: selectedMenu === 'logs' }" @click="selectMenu('logs')">
          <span>执行日志</span>
          <b>{{ commandLogs.length }}</b>
        </button>
      </div>
    </aside>

    <section class="content">
      <header class="topbar">
        <div>
          <p class="eyebrow">WINDOWS FORWARD</p>
          <h1>{{ selectedMenu === 'logs' ? '执行日志' : activeType.label }}</h1>
          <p class="subtitle">{{ selectedMenu === 'logs' ? '集中查看所有启用、禁用和系统查询命令的执行记录。' : activeType.hint }}</p>
        </div>
        <div class="status-board">
          <span>配置规则 <b>{{ rules.length }}</b></span>
          <span>当前启用 <b>{{ enabledCount }}</b></span>
          <span>当前禁用 <b>{{ disabledCount }}</b></span>
          <span>失败日志 <b>{{ failedLogCount }}</b></span>
        </div>
      </header>

      <template v-if="selectedMenu !== 'logs'">
        <section class="workspace">
          <el-card class="editor" shadow="never">
            <template #header>
              <div class="card-head">
                <div>
                  <strong>{{ editingId ? '编辑规则' : `新建${activeType.label}` }}</strong>
                  <p>规则会先保存到数据库，启用时再执行系统命令。</p>
                </div>
                <el-button :icon="Refresh" text @click="resetForm">清空</el-button>
              </div>
            </template>

            <el-form ref="formRef" :model="form" :rules="ruleset" label-position="top">
              <div class="form-grid">
                <el-form-item label="规则名称" prop="name">
                  <el-input v-model="form.name" maxlength="80" placeholder="例如：内网 Web 转发" />
                </el-form-item>
                <el-form-item label="规则类型" prop="type">
                  <el-select v-model="form.type" class="full">
                    <el-option v-for="item in typeOptions" :key="item.value" :label="item.label" :value="item.value" />
                  </el-select>
                </el-form-item>
              </div>

              <el-form-item label="说明">
                <el-input v-model="form.description" type="textarea" :rows="2" placeholder="可选，记录用途或负责人。" />
              </el-form-item>

              <div v-if="isPortLike" class="form-grid">
                <el-form-item v-if="form.type === ForwardRuleType.PortProxy" label="监听地址">
                  <el-input v-model="form.listenAddress" placeholder="0.0.0.0" />
                </el-form-item>
                <el-form-item label="监听/外部端口" prop="listenPort">
                  <el-input-number v-model="form.listenPort" :min="1" :max="65535" class="full" />
                </el-form-item>
                <el-form-item v-if="form.type === ForwardRuleType.Firewall || form.type === ForwardRuleType.Nat" label="协议">
                  <el-segmented v-model="form.protocol" :options="protocolOptions" />
                </el-form-item>
              </div>

              <div v-if="needsTarget" class="form-grid">
                <el-form-item label="目标/内部地址">
                  <el-input v-model="form.connectAddress" placeholder="192.168.1.10" />
                </el-form-item>
                <el-form-item label="目标/内部端口" prop="connectPort">
                  <el-input-number v-model="form.connectPort" :min="1" :max="65535" class="full" />
                </el-form-item>
              </div>

              <div v-if="form.type === ForwardRuleType.Nat" class="form-grid">
                <el-form-item label="NAT 名称">
                  <el-input v-model="form.natName" placeholder="MyNAT" />
                </el-form-item>
                <el-form-item label="内部网段 Prefix">
                  <el-input v-model="form.prefix" placeholder="192.168.100.0/24，可选；NAT 不存在时用于自动创建" />
                </el-form-item>
              </div>

              <div v-if="needsRoute" class="form-grid three">
                <el-form-item label="目标网段">
                  <el-input v-model="form.routeDestination" placeholder="10.0.0.0" />
                </el-form-item>
                <el-form-item label="子网掩码">
                  <el-input v-model="form.routeMask" placeholder="255.255.255.0" />
                </el-form-item>
                <el-form-item label="网关">
                  <el-input v-model="form.routeGateway" placeholder="192.168.1.1" />
                </el-form-item>
              </div>

              <div v-if="needsSsh" class="form-grid">
                <el-form-item label="SSH 主机">
                  <el-input v-model="form.sshHost" placeholder="server.example.com" />
                </el-form-item>
                <el-form-item label="SSH 用户">
                  <el-input v-model="form.sshUser" placeholder="可选，例如 administrator" />
                </el-form-item>
              </div>

              <el-alert
                title="提示：启用/禁用系统转发通常需要管理员权限；请用管理员身份运行后端服务。"
                type="warning"
                :closable="false"
                show-icon
              />

              <div class="actions">
                <el-button :icon="View" @click="previewCommand">验证并预览命令</el-button>
                <el-button type="primary" :loading="saving" :icon="Connection" @click="save">
                  {{ editingId ? '保存修改' : '保存规则' }}
                </el-button>
              </div>
            </el-form>

            <pre v-if="preview" class="preview">{{ preview }}</pre>
          </el-card>

          <el-card class="rules" shadow="never">
            <template #header>
              <div class="stack-head">
                <div class="card-head">
                  <div>
                    <strong>{{ activeType.label }}规则</strong>
                    <p>右侧状态来自数据库配置和操作系统实际规则的合并结果。</p>
                  </div>
                  <el-button :icon="Refresh" :loading="loading || systemLoading" @click="refreshDashboard">刷新全部</el-button>
                </div>
                <div class="rule-toolbar">
                  <el-input v-model="ruleQuery" :prefix-icon="Search" clearable placeholder="搜索规则名称、类型或地址" />
                  <el-segmented
                    v-model="ruleStatus"
                    :options="[
                      { label: '全部', value: 'all' },
                      { label: '已启用', value: 'enabled' },
                      { label: '已禁用', value: 'disabled' }
                    ]"
                  />
                </div>
              </div>
            </template>

            <el-table v-loading="loading || systemLoading" :data="visibleRows" row-key="key" empty-text="暂无匹配规则。">
              <el-table-column label="状态" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.actualEnabled ? 'success' : 'info'" effect="dark">
                    {{ row.actualEnabled ? '已启用' : '已禁用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="来源" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.source === 'database' ? 'primary' : 'warning'">
                    {{ row.source === 'database' ? '数据库' : '操作系统' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="规则">
                <template #default="{ row }">
                  <div class="rule-name">{{ row.name }}</div>
                  <div class="rule-meta">{{ row.detail }}</div>
                </template>
              </el-table-column>
              <el-table-column label="更新时间" width="180">
                <template #default="{ row }">{{ row.updatedAt ? new Date(row.updatedAt).toLocaleString() : '-' }}</template>
              </el-table-column>
              <el-table-column label="操作" width="310" fixed="right">
                <template #default="{ row }">
                  <template v-if="row.rule">
                    <el-button
                      size="small"
                      :type="row.actualEnabled ? 'warning' : 'success'"
                      :icon="SwitchButton"
                      :loading="applyingKey === row.key"
                      @click="toggle(row)"
                    >
                      {{ row.actualEnabled ? '禁用' : '启用' }}
                    </el-button>
                    <el-button size="small" :icon="Edit" :disabled="row.actualEnabled" @click="edit(row)">编辑</el-button>
                    <el-button size="small" type="danger" :icon="Delete" :disabled="row.actualEnabled" @click="remove(row)">删除</el-button>
                  </template>
                  <template v-else>
                    <el-button size="small" type="primary" :loading="applyingKey === row.key" @click="importSystemRule(row)">保存到数据库</el-button>
                    <el-button size="small" type="warning" :loading="applyingKey === row.key" @click="disableImportedSystemRule(row)">禁用并入库</el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </section>
      </template>

      <template v-else>
        <el-card class="log-panel" shadow="never">
          <template #header>
            <div class="card-head">
              <div>
                <strong>执行日志</strong>
                <p>统一查看启用、禁用和系统查询命令的返回码、命令文本与输出。</p>
              </div>
              <el-button :icon="Refresh" :loading="logsLoading" @click="refreshLogs">刷新日志</el-button>
            </div>
          </template>

          <div v-if="latestLog" class="latest-log">
            <el-tag :type="latestLog.success ? 'success' : 'danger'" effect="dark">
              最近{{ latestLog.success ? '成功' : '失败' }}
            </el-tag>
            <span>{{ latestLog.ruleName }} · {{ latestLog.action }} · {{ new Date(latestLog.executedAt).toLocaleString() }}</span>
          </div>

          <el-table v-loading="logsLoading" :data="commandLogs" row-key="id" empty-text="暂无命令执行记录。">
            <el-table-column type="expand">
              <template #default="{ row }">
                <div class="log-detail">
                  <div class="detail-label">命令</div>
                  <pre class="command-code">{{ row.commandText }}</pre>
                  <div class="detail-label">输出</div>
                  <pre class="log-output">{{ row.output || '无输出。' }}</pre>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="结果" width="92">
              <template #default="{ row }">
                <el-tag :type="row.success ? 'success' : 'danger'" effect="dark">
                  {{ row.success ? '成功' : '失败' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="规则 / 动作" min-width="220">
              <template #default="{ row }">
                <div class="rule-name">{{ row.ruleName || '未关联规则' }}</div>
                <div class="rule-meta">{{ row.action }} · {{ row.message }}</div>
              </template>
            </el-table-column>
            <el-table-column label="返回码" width="90">
              <template #default="{ row }">{{ row.exitCode ?? '-' }}</template>
            </el-table-column>
            <el-table-column label="执行时间" width="180">
              <template #default="{ row }">{{ new Date(row.executedAt).toLocaleString() }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </template>
    </section>
  </main>
</template>

<style scoped>
.app-shell {
  display: grid;
  grid-template-columns: 260px minmax(0, 1fr);
  min-height: 100vh;
}

.side-nav {
  position: sticky;
  top: 0;
  height: 100vh;
  padding: 24px 16px;
  border-right: 1px solid var(--line);
  background: rgba(255, 255, 250, 0.72);
  backdrop-filter: blur(16px);
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 28px;
}

.brand p {
  margin: 0;
  color: var(--accent);
  font-size: 11px;
  font-weight: 850;
  letter-spacing: 0.16em;
}

.brand strong {
  color: var(--ink);
  font-size: 22px;
}

.app-logo {
  width: 52px;
  height: 52px;
  filter: drop-shadow(0 14px 20px rgba(19, 35, 30, 0.18));
}

.menu-section {
  display: grid;
  gap: 8px;
  margin-top: 22px;
}

.menu-section > span {
  padding: 0 10px;
  color: var(--muted);
  font-size: 12px;
  font-weight: 760;
}

.menu-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  width: 100%;
  min-height: 42px;
  padding: 0 12px;
  color: var(--ink);
  background: transparent;
  border: 1px solid transparent;
  border-radius: 8px;
  cursor: pointer;
}

.menu-item:hover,
.menu-item.active {
  background: rgba(19, 124, 91, 0.1);
  border-color: rgba(19, 124, 91, 0.18);
}

.menu-item b {
  min-width: 28px;
  padding: 2px 8px;
  color: var(--accent);
  background: rgba(19, 124, 91, 0.1);
  border-radius: 999px;
}

.content {
  width: min(100%, 1480px);
  padding: 28px 24px 48px;
}

.topbar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 24px;
  align-items: center;
  margin-bottom: 20px;
}

.eyebrow {
  margin: 0 0 8px;
  color: var(--accent);
  font-size: 12px;
  font-weight: 850;
  letter-spacing: 0.16em;
}

h1 {
  margin: 0;
  color: var(--ink);
  font-size: clamp(32px, 4vw, 58px);
  line-height: 0.96;
}

.subtitle {
  max-width: 760px;
  margin: 14px 0 0;
  color: var(--muted);
  font-size: 16px;
  line-height: 1.7;
}

.status-board {
  display: grid;
  grid-template-columns: repeat(4, 108px);
  border: 1px solid var(--line);
  background: rgba(255, 255, 250, 0.68);
  box-shadow: 0 18px 42px rgba(23, 33, 29, 0.08);
}

.status-board span {
  padding: 16px;
  color: var(--muted);
  border-left: 1px solid var(--line);
}

.status-board span:first-child {
  border-left: 0;
}

.status-board b {
  display: block;
  margin-top: 6px;
  color: var(--ink);
  font-size: 28px;
}

.workspace {
  display: grid;
  grid-template-columns: minmax(390px, 0.82fr) minmax(680px, 1.38fr);
  gap: 18px;
  align-items: start;
}

.editor,
.rules,
.log-panel {
  border: 1px solid var(--line);
  background: var(--panel);
  backdrop-filter: blur(14px);
}

.stack-head {
  display: grid;
  gap: 14px;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.card-head strong {
  color: var(--ink);
  font-size: 18px;
}

.card-head p {
  margin: 6px 0 0;
  color: var(--muted);
  font-size: 13px;
}

.rule-toolbar {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) auto;
  gap: 12px;
  align-items: center;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.form-grid.three {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.full {
  width: 100%;
}

.actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 18px;
}

.preview,
.command-code,
.log-output {
  margin: 18px 0 0;
  padding: 12px;
  white-space: pre-wrap;
  word-break: break-word;
  color: #f4f2e8;
  background: #14211d;
  border-radius: 8px;
  line-height: 1.55;
}

.log-detail {
  padding: 8px 12px 16px 48px;
}

.detail-label {
  margin: 10px 0 6px;
  color: var(--muted);
  font-size: 12px;
  font-weight: 760;
}

.log-output {
  max-height: 240px;
  overflow: auto;
}

.latest-log {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  color: var(--muted);
}

.rule-name {
  color: var(--ink);
  font-weight: 760;
}

.rule-meta {
  margin-top: 5px;
  color: var(--muted);
  font-size: 12px;
}

@media (max-width: 1180px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .side-nav {
    position: static;
    height: auto;
    border-right: 0;
    border-bottom: 1px solid var(--line);
  }

  .menu-section {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .menu-section > span {
    grid-column: 1 / -1;
  }

  .topbar,
  .workspace {
    grid-template-columns: 1fr;
  }

  .status-board {
    width: fit-content;
  }
}

@media (max-width: 720px) {
  .content {
    padding: 18px 12px 36px;
  }

  .menu-section,
  .form-grid,
  .form-grid.three,
  .rule-toolbar {
    grid-template-columns: 1fr;
  }

  .status-board {
    grid-template-columns: 1fr 1fr;
    width: 100%;
  }
}
</style>
