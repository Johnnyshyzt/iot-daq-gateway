import { createFileRoute } from '@tanstack/react-router'
import { AlarmsPage } from '@/features/viz/alarms-page'

export const Route = createFileRoute('/_authenticated/alarms')({
  component: AlarmsPage,
})
