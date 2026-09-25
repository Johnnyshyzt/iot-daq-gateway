import {
  Activity,
  Command,
  LayoutDashboard,
  Radio,
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
  teams: [
    {
      name: 'Config Studio',
      logo: Command,
      plan: '采集网关',
    },
  ],
  navGroups: [
    {
      title: '采集',
      items: [
        { title: '概览', url: '/', icon: LayoutDashboard },
        { title: '设备', url: '/devices', icon: Cpu },
        { title: '点位', url: '/points', icon: Activity },
        { title: '北向 MQTT', url: '/sinks/mqtt', icon: Share2 },
        { title: '发布', url: '/publish', icon: Upload },
        { title: '运行态', url: '/runtime', icon: Radio },
      ],
    },
    {
      title: '系统',
      items: [{ title: '系统', url: '/settings', icon: Settings }],
    },
  ],
}
