import { createFileRoute, redirect } from '@tanstack/react-router'
import { AuthenticatedLayout } from '@/components/layout/authenticated-layout'
import { readMustChange, useAuthStore } from '@/stores/auth-store'

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ({ location }) => {
    const state = useAuthStore.getState().auth
    if (!state.accessToken) {
      throw redirect({
        to: '/sign-in',
        search: { redirect: location.pathname },
      })
    }
    const mustChange = state.user?.mustChangePassword || readMustChange()
    if (mustChange && location.pathname !== '/account/password') {
      throw redirect({ to: '/account/password' })
    }
  },
  component: AuthenticatedLayout,
})
