import { createFileRoute, redirect } from '@tanstack/react-router'
import { HttpsSettingsPage } from '@/features/daq/https-settings'
import { useAuthStore } from '@/stores/auth-store'

export const Route = createFileRoute('/_authenticated/https')({
  beforeLoad: () => {
    if (useAuthStore.getState().auth.user?.role[0] !== 'admin') {
      throw redirect({ to: '/' })
    }
  },
  component: HttpsSettingsPage,
})
