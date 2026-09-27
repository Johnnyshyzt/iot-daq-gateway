import {
  Activity,
  Bell,
  BookOpen,
  Gauge,
  LayoutDashboard,
  LineChart,
  ListChecks,
  PieChart,
  Radio,
  KeyRound,
  ScrollText,
  Settings,
  Share2,
  Upload,
  Cpu,
} from 'lucide-react'
import { type SidebarData } from '../types'

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
        { title: '稼动率', url: '/utilization', icon: PieChart },
        { title: '设备', url: '/devices', icon: Cpu },
        { title: '点位模板', url: '/points', icon: Activity },
        { title: '品牌目录', url: '/catalog', icon: BookOpen },
        { title: '实时值', url: '/live', icon: Gauge },
        { title: '北向 MQTT', url: '/sinks/mqtt', icon: Share2 },
        { title: '发布', url: '/publish', icon: Upload },
        { title: '运行态', url: '/runtime', icon: Radio },
      ],
    },
    {
      title: '系统',
      items: [
        { title: '系统', url: '/settings', icon: Settings },
        { title: '审计', url: '/audit', icon: ScrollText },
        { title: '修改密码', url: '/account/password', icon: KeyRound },
      ],
    },
  ],
}
