import { createFileRoute } from '@tanstack/react-router'
import { DashboardPage } from '@/features/viz/dashboard'

export const Route = createFileRoute('/_authenticated/board')({
  component: function BoardRoute() {
    return <DashboardPage kiosk />
  },
})
