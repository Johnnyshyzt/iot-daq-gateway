import { ConfigDrawer } from '@/components/config-drawer'
import { Header } from '@/components/layout/header'
import { Main } from '@/components/layout/main'
import { ProfileDropdown } from '@/components/profile-dropdown'
import { Search } from '@/components/search'
import { ThemeSwitch } from '@/components/theme-switch'

export function PageShell({
  title,
  description,
  actions,
  children,
  fluid,
  className,
}: {
  title: string
  description?: string
  actions?: React.ReactNode
  children: React.ReactNode
  fluid?: boolean
  className?: string
}) {
  return (
    <>
      <Header>
        <Search className='me-auto' placeholder='搜索' />
        <ThemeSwitch />
        <ConfigDrawer />
        <ProfileDropdown />
      </Header>
      <Main fluid={fluid} className={className}>
        <div className='mb-4 flex flex-wrap items-end justify-between gap-3'>
          <div>
            <h1 className='text-2xl font-bold tracking-tight'>{title}</h1>
            {description ? (
              <p className='text-sm text-muted-foreground'>{description}</p>
            ) : null}
          </div>
          {actions}
        </div>
        {children}
      </Main>
    </>
  )
}
