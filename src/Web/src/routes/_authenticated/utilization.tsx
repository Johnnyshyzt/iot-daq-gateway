import { createFileRoute } from '@tanstack/react-router'
import { UtilizationPage } from '@/features/viz/utilization-page'

export const Route = createFileRoute('/_authenticated/utilization')({
  component: UtilizationPage,
})
