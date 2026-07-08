import axios from 'axios'

export enum ForwardRuleType {
  PortProxy = 0,
  Firewall = 1,
  Nat = 2,
  Route = 3,
  IpForwarding = 4,
  SshLocal = 5,
  SshRemote = 6,
  SshDynamic = 7
}

export enum ForwardProtocol {
  Tcp = 0,
  Udp = 1
}

export interface ForwardRuleInput {
  name: string
  description?: string
  type: ForwardRuleType
  protocol: ForwardProtocol
  listenAddress?: string
  listenPort?: number
  connectAddress?: string
  connectPort?: number
  natName?: string
  prefix?: string
  routeDestination?: string
  routeMask?: string
  routeGateway?: string
  sshHost?: string
  sshUser?: string
}

export interface ForwardRule extends ForwardRuleInput {
  id: number
  enabled: boolean
  runtimeProcessId?: number
  createdAt: string
  updatedAt: string
  lastAppliedAt?: string
}

export interface ApiResponse<T> {
  success: boolean
  message: string
  data?: T
}

export interface FieldError {
  field: string
  message: string
}

export interface CommandExecutionLog {
  id: number
  forwardRuleId?: number
  ruleName?: string
  action: string
  commandText: string
  success: boolean
  exitCode?: number
  message: string
  output?: string
  executedAt: string
}

export async function listRules() {
  const { data } = await axios.get<ForwardRule[]>('/api/rules')
  return data
}

export async function createRule(input: ForwardRuleInput) {
  const { data } = await axios.post<ForwardRule>('/api/rules', input)
  return data
}

export async function updateRule(id: number, input: ForwardRuleInput) {
  const { data } = await axios.put<ForwardRule>(`/api/rules/${id}`, input)
  return data
}

export async function deleteRule(id: number) {
  const { data } = await axios.delete<ApiResponse<null>>(`/api/rules/${id}`)
  return data
}

export async function enableRule(id: number) {
  const { data } = await axios.post<ApiResponse<ForwardRule>>(`/api/rules/${id}/enable`)
  return data
}

export async function disableRule(id: number) {
  const { data } = await axios.post<ApiResponse<ForwardRule>>(`/api/rules/${id}/disable`)
  return data
}

export async function validateRule(input: ForwardRuleInput) {
  const { data } = await axios.post<ApiResponse<string>>('/api/rules/validate', input)
  return data
}

export async function listCommandLogs(take = 30) {
  const { data } = await axios.get<CommandExecutionLog[]>('/api/command-logs', {
    params: { take }
  })
  return data
}
