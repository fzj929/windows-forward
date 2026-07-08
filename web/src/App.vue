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
  showPortProxy,
  updateRule,
  validateRule,
  type CommandResult,
  type CommandExecutionLog,
  type FieldError,
  type ForwardRule,
  type ForwardRuleInput
} from './api/rules'

const typeOptions = [
  { label: '端口转发 portproxy', value: ForwardRuleType.PortProxy, hint: '本机端口转发到另一台主机，适合 TCP 服务映射。' },
  { label: '防火墙放行', value: ForwardRuleType.Firewall, hint: '创建入站防火墙规则，放行指定端口。' },
  { label: 'NAT 静态映射', value: ForwardRuleType.Nat, hint: '通过 NetNat 添加外部端口到内部地址的映射。' },
  { label: '静态路由', value: ForwardRuleType.Route, hint: '添加或删除永久路由。' },
  { label: 'IP 转发开关', value: ForwardRuleType.IpForwarding, hint: '开启 Windows IPEnableRouter。' },
  { label: 'SSH 本地转发', value: ForwardRuleType.SshLocal, hint: 'ssh -L，本机端口转发到 SSH 侧可访问的目标。' },
  { label: 'SSH 远程转发', value: ForwardRuleType.SshRemote, hint: 'ssh -R，远端端口转发到本机可访问的目标。' },
  { label: 'SSH 动态 SOCKS', value: ForwardRuleType.SshDynamic, hint: 'ssh -D，启动 SOCKS 代理。' }
]

const protocolOptions = [
  { label: 'TCP', value: ForwardProtocol.Tcp },
  { label: 'UDP', value: ForwardProtocol.Udp }
]

const rules = ref<ForwardRule[]>([])
const commandLogs = ref<CommandExecutionLog[]>([])
const portProxyResult = ref<CommandResult>()
const loading = ref(false)
const logsLoading = ref(false)
const portProxyLoading = ref(false)
const saving = ref(false)
const applyingId = ref<number>()
const preview = ref('')
const formRef = ref<FormInstance>()
const editingId = ref<number>()
const ruleQuery = ref('')
const ruleStatus = ref<'all' | 'enabled' | 'disabled'>('all')

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

const activeType = computed(() => typeOptions.find(x => x.value === form.type) ?? typeOptions[0])
const isPortLike = computed(() => [ForwardRuleType.PortProxy, ForwardRuleType.Firewall, ForwardRuleType.Nat, ForwardRuleType.SshLocal, ForwardRuleType.SshRemote, ForwardRuleType.SshDynamic].includes(form.type))
const needsTarget = computed(() => [ForwardRuleType.PortProxy, ForwardRuleType.Nat, ForwardRuleType.SshLocal, ForwardRuleType.SshRemote].includes(form.type))
const needsSsh = computed(() => [ForwardRuleType.SshLocal, ForwardRuleType.SshRemote, ForwardRuleType.SshDynamic].includes(form.type))
const needsRoute = computed(() => form.type === ForwardRuleType.Route)
const enabledCount = computed(() => rules.value.filter(x => x.enabled).length)
const disabledCount = computed(() => rules.value.length - enabledCount.value)
const latestLog = computed(() => commandLogs.value[0])
const failedLogCount = computed(() => commandLogs.value.filter(x => !x.success).length)
const visibleRules = computed(() => {
  const keyword = ruleQuery.value.trim().toLowerCase()
  return rules.value.filter(rule => {
    const statusMatched =
      ruleStatus.value === 'all' ||
      (ruleStatus.value === 'enabled' && rule.enabled) ||
      (ruleStatus.value === 'disabled' && !rule.enabled)
    const keywordMatched =
      !keyword ||
      rule.name.toLowerCase().includes(keyword) ||
      describe(rule).toLowerCase().includes(keyword) ||
      typeName(rule.type).toLowerCase().includes(keyword)
    return statusMatched && keywordMatched
  })
})

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
  preview.value = ''
  formRef.value?.clearValidate()
}

function typeName(type: ForwardRuleType) {
  return typeOptions.find(x => x.value === type)?.label ?? '未知'
}

function protocolName(protocol: ForwardProtocol) {
  return protocol === ForwardProtocol.Udp ? 'UDP' : 'TCP'
}

function describe(rule: ForwardRule) {
  if (rule.type === ForwardRuleType.Route) return `${rule.routeDestination}/${rule.routeMask} -> ${rule.routeGateway}`
  if (rule.type === ForwardRuleType.IpForwarding) return 'Windows IP 转发总开关'
  if (rule.type === ForwardRuleType.SshDynamic) return `SOCKS :${rule.listenPort} -> ${rule.sshHost}`
  if (rule.type === ForwardRuleType.Firewall) return `${protocolName(rule.protocol)} :${rule.listenPort}`
  return `${rule.listenAddress ?? '0.0.0.0'}:${rule.listenPort} -> ${rule.connectAddress}:${rule.connectPort}`
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

async function refresh() {
  loading.value = true
  try {
    rules.value = await listRules()
  } finally {
    loading.value = false
  }
}

async function refreshDashboard() {
  await Promise.all([refresh(), refreshLogs(), refreshPortProxy()])
}

async function refreshLogs() {
  logsLoading.value = true
  try {
    commandLogs.value = await listCommandLogs(30)
  } finally {
    logsLoading.value = false
  }
}

async function refreshPortProxy() {
  portProxyLoading.value = true
  try {
    const result = await showPortProxy()
    portProxyResult.value = result.data
    await refreshLogs()
  } catch (error) {
    surfaceError(error)
  } finally {
    portProxyLoading.value = false
  }
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
    await refresh()
  } catch (error) {
    surfaceError(error)
  } finally {
    saving.value = false
  }
}

function edit(rule: ForwardRule) {
  if (rule.enabled) {
    ElMessage.warning('请先禁用规则，再修改配置。')
    return
  }
  editingId.value = rule.id
  Object.assign(form, cloneForm(rule))
  preview.value = ''
}

async function toggle(rule: ForwardRule) {
  applyingId.value = rule.id
  try {
    const result = rule.enabled ? await disableRule(rule.id) : await enableRule(rule.id)
    ElMessage.success(result.message)
    await refresh()
    await refreshLogs()
    await refreshPortProxy()
  } catch (error) {
    surfaceError(error)
  } finally {
    applyingId.value = undefined
  }
}

async function remove(rule: ForwardRule) {
  await ElMessageBox.confirm(`确认删除规则“${rule.name}”？`, '删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消'
  })
  try {
    const result = await deleteRule(rule.id)
    ElMessage.success(result.message)
    await refresh()
  } catch (error) {
    surfaceError(error)
  }
}

onMounted(refreshDashboard)
</script>

<template>
  <main class="shell">
    <section class="hero">
      <div class="brand-block">
        <img class="app-logo" src="/windows-forward.svg" alt="Windows Forward" />
        <div>
          <p class="eyebrow">WINDOWS FORWARD</p>
          <h1>转发规则控制台</h1>
          <p class="subtitle">把 Windows 端口转发、防火墙、NAT、路由和 SSH 隧道放在一个可审计的操作台里。</p>
        </div>
      </div>
      <div class="status-board">
        <span>规则总数 <b>{{ rules.length }}</b></span>
        <span>已启用 <b>{{ enabledCount }}</b></span>
        <span>待启用 <b>{{ disabledCount }}</b></span>
        <span>失败日志 <b>{{ failedLogCount }}</b></span>
      </div>
    </section>

    <section class="workspace">
      <el-card class="editor" shadow="never">
        <template #header>
          <div class="card-head">
            <div>
              <strong>{{ editingId ? '编辑规则' : '新建规则' }}</strong>
              <p>{{ activeType.hint }}</p>
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
                <strong>规则列表</strong>
                <p>启用会执行对应 Windows 命令，禁用会删除或关闭对应配置。</p>
              </div>
              <el-button :icon="Refresh" @click="refreshDashboard">刷新全部</el-button>
            </div>
            <div class="rule-toolbar">
              <el-input v-model="ruleQuery" :prefix-icon="Search" clearable placeholder="搜索规则名称、类型或地址" />
              <el-segmented
                v-model="ruleStatus"
                :options="[
                  { label: '全部', value: 'all' },
                  { label: '启用', value: 'enabled' },
                  { label: '禁用', value: 'disabled' }
                ]"
              />
            </div>
          </div>
        </template>

        <el-table v-loading="loading" :data="visibleRules" row-key="id" empty-text="暂无匹配规则。">
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="row.enabled ? 'success' : 'info'" effect="dark">{{ row.enabled ? '启用' : '禁用' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="规则">
            <template #default="{ row }">
              <div class="rule-name">{{ row.name }}</div>
              <div class="rule-meta">{{ typeName(row.type) }} · {{ describe(row) }}</div>
            </template>
          </el-table-column>
          <el-table-column label="更新时间" width="180">
            <template #default="{ row }">{{ new Date(row.updatedAt).toLocaleString() }}</template>
          </el-table-column>
          <el-table-column label="操作" width="260" fixed="right">
            <template #default="{ row }">
              <el-button
                size="small"
                :type="row.enabled ? 'warning' : 'success'"
                :icon="SwitchButton"
                :loading="applyingId === row.id"
                @click="toggle(row)"
              >
                {{ row.enabled ? '禁用' : '启用' }}
              </el-button>
              <el-button size="small" :icon="Edit" :disabled="row.enabled" @click="edit(row)">编辑</el-button>
              <el-button size="small" type="danger" :icon="Delete" :disabled="row.enabled" @click="remove(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </section>

    <section class="operations-grid">
      <el-card class="portproxy-panel" shadow="never">
        <template #header>
          <div class="card-head">
            <div>
              <strong>系统 portproxy 配置</strong>
              <p>执行 <code>netsh interface portproxy show all</code>，查看 Windows 当前真实生效的端口转发规则。</p>
            </div>
            <el-button :icon="Refresh" :loading="portProxyLoading" @click="refreshPortProxy">刷新配置</el-button>
          </div>
        </template>

        <div class="portproxy-status">
          <el-tag :type="portProxyResult?.success ? 'success' : 'info'" effect="dark">
            {{ portProxyResult ? `返回码 ${portProxyResult.exitCode ?? '-'}` : '未执行' }}
          </el-tag>
          <span>{{ portProxyResult?.message ?? '点击刷新配置读取系统 portproxy。' }}</span>
        </div>
        <pre class="command-code state-output">{{ portProxyResult?.output?.trim() || '当前没有读取到 portproxy 规则。' }}</pre>
      </el-card>

      <el-card class="log-panel" shadow="never">
        <template #header>
          <div class="card-head">
            <div>
              <strong>最近执行命令</strong>
              <p>记录实际执行的 Windows 命令、返回码和输出。</p>
            </div>
            <el-button :icon="Refresh" @click="refreshLogs">刷新日志</el-button>
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
    </section>
  </main>
</template>

<style scoped>
.shell {
  width: min(1480px, calc(100vw - 48px));
  margin: 0 auto;
  padding: 34px 0 48px;
}

.hero {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(440px, auto);
  align-items: center;
  gap: 28px;
  padding: 22px 0 28px;
}

.brand-block {
  display: flex;
  align-items: center;
  gap: 18px;
}

.app-logo {
  width: 74px;
  height: 74px;
  flex: 0 0 auto;
  filter: drop-shadow(0 18px 26px rgba(19, 35, 30, 0.18));
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
  font-size: clamp(34px, 5vw, 68px);
  line-height: 0.96;
}

.subtitle {
  max-width: 760px;
  margin: 18px 0 0;
  color: var(--muted);
  font-size: 17px;
  line-height: 1.7;
}

.status-board {
  display: grid;
  grid-template-columns: repeat(4, 112px);
  border: 1px solid var(--line);
  background: rgba(255, 255, 250, 0.68);
  box-shadow: 0 18px 42px rgba(23, 33, 29, 0.08);
}

.status-board span {
  padding: 18px;
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
  font-size: 30px;
}

.stack-head {
  display: grid;
  gap: 14px;
}

.rule-toolbar {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) auto;
  gap: 12px;
  align-items: center;
}

.workspace {
  display: grid;
  grid-template-columns: minmax(390px, 0.82fr) minmax(680px, 1.38fr);
  gap: 18px;
  align-items: start;
}

.operations-grid {
  display: grid;
  grid-template-columns: minmax(390px, 0.82fr) minmax(680px, 1.38fr);
  gap: 18px;
  align-items: start;
  margin-top: 18px;
}

.editor,
.rules,
.portproxy-panel,
.log-panel {
  border: 1px solid var(--line);
  background: var(--panel);
  backdrop-filter: blur(14px);
}

.log-panel {
  min-width: 0;
}

.portproxy-panel {
  min-width: 0;
}

.portproxy-status {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  color: var(--muted);
}

.latest-log {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  color: var(--muted);
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

.preview {
  margin: 18px 0 0;
  padding: 14px;
  overflow: auto;
  color: #f4f2e8;
  background: #14211d;
  border-radius: 8px;
  line-height: 1.6;
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

.command-code,
.log-output {
  margin: 0;
  padding: 12px;
  white-space: pre-wrap;
  word-break: break-word;
  color: #f4f2e8;
  background: #14211d;
  border-radius: 8px;
  line-height: 1.55;
}

.state-output {
  min-height: 168px;
  max-height: 300px;
  overflow: auto;
}

.log-output {
  max-height: 240px;
  overflow: auto;
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

@media (max-width: 1120px) {
  .workspace,
  .operations-grid,
  .hero {
    grid-template-columns: 1fr;
  }

  .status-board {
    width: fit-content;
  }
}

@media (max-width: 720px) {
  .shell {
    width: min(100% - 24px, 1480px);
    padding-top: 20px;
  }

  .form-grid,
  .form-grid.three,
  .rule-toolbar {
    grid-template-columns: 1fr;
  }

  .status-board {
    grid-template-columns: 1fr 1fr;
    width: 100%;
  }

  .brand-block {
    align-items: flex-start;
  }

  .app-logo {
    width: 52px;
    height: 52px;
  }
}
</style>
