import { useLayout } from '@/context/layout-provider'
import { roleLabel } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from '@/components/ui/sidebar'
import { sidebarData } from './data/sidebar-data'
import { NavGroup } from './nav-group'
import { NavUser } from './nav-user'
import { ProductMark } from './product-mark'

export function AppSidebar() {
  const { collapsible, variant } = useLayout()
  const account = useAuthStore((state) => state.auth.user)
  const user = {
    name: account?.email || sidebarData.user.name,
    email: account?.role[0] ? roleLabel(account.role[0]) : sidebarData.user.email,
    avatar: sidebarData.user.avatar,
  }
  return (
    <Sidebar collapsible={collapsible} variant={variant}>
      <SidebarHeader>
        <ProductMark />
      </SidebarHeader>
      <SidebarContent>
        {sidebarData.navGroups.map((props) => (
          <NavGroup key={props.title} {...props} />
        ))}
      </SidebarContent>
      <SidebarFooter>
        <NavUser user={user} />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
