import {
  Activity,
  Bell,
  BellRing,
  BookOpen,
  Inbox,
  Gauge,
  LayoutDashboard,
  LineChart,
  ListChecks,
  PieChart,
  Radio,
  KeyRound,
  Cable,
  Webhook,
  ScrollText,
  Settings,
  Share2,
  Upload,
  Cpu,
  BadgeCheck,
  Package,
  Stethoscope,
  Users,
  Lock,
} from 'lucide-react'
import { type NavGroup, type NavItem, type SidebarData } from '../types'

export const sidebarData: SidebarData = {
  user: {
    name: '未登录',
    email: '',
    avatar: '',
  },
  navGroups: [
    {
      title: '采集',
      items: [
        { title: '上手引导', url: '/setup', icon: ListChecks },
        { title: '总览', url: '/', icon: LayoutDashboard },
        { title: '历史曲线', url: '/history', icon: LineChart },
        { title: '报警', url: '/alarms', icon: Bell },
        { title: '通知', url: '/notify', icon: BellRing },
        { title: '通知记录', url: '/notify/history', icon: Inbox },
        { title: '稼动率', url: '/utilization', icon: PieChart },
        { title: '设备', url: '/devices', icon: Cpu },
        { title: '点位模板', url: '/points', icon: Activity },
        { title: '品牌目录', url: '/catalog', icon: BookOpen },
        { title: '实时值', url: '/live', icon: Gauge },
        { title: '北向 MQTT', url: '/sinks/mqtt', icon: Share2 },
        { title: '北向 HTTP', url: '/sinks/http', icon: Webhook },
        { title: '查询接口', url: '/integrate/query', icon: KeyRound },
        { title: 'OPC UA', url: '/integrate/opcua', icon: Cable },
        { title: '发布', url: '/publish', icon: Upload },
        { title: '运行态', url: '/runtime', icon: Radio },
        { title: '现场调试', url: '/commission', icon: Stethoscope },
      ],
    },
    {
      title: '系统',
      items: [
        { title: '系统', url: '/settings', icon: Settings },
        { title: '用户', url: '/users', icon: Users, adminOnly: true },
        { title: 'HTTPS', url: '/https', icon: Lock, adminOnly: true },
        { title: '授权许可', url: '/license', icon: BadgeCheck },
        { title: '升级', url: '/upgrade', icon: Package },
        { title: '审计', url: '/audit', icon: ScrollText },
        { title: '修改密码', url: '/account/password', icon: KeyRound },
      ],
    },
  ],
}

export function sidebarForRole(role: string | undefined): SidebarData {
  const admin = role === 'admin'
  const navGroups: NavGroup[] = sidebarData.navGroups
    .map((group) => ({ ...group, items: filterItems(group.items, admin) }))
    .filter((group) => group.items.length > 0)
  return { ...sidebarData, navGroups }
}

function filterItems(items: NavItem[], admin: boolean): NavItem[] {
  const next: NavItem[] = []
  for (const item of items) {
    if (item.items) {
      const children = item.items.filter((child) => admin || !child.adminOnly)
      if (children.length > 0) next.push({ ...item, items: children })
    } else if (admin || !item.adminOnly) {
      next.push(item)
    }
  }
  return next
}
