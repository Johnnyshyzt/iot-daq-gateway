import { createFileRoute } from '@tanstack/react-router'
import { DowntimePage } from '@/features/oee/downtime-page'

export const Route = createFileRoute('/_authenticated/downtime')({
  component: DowntimePage,
})
