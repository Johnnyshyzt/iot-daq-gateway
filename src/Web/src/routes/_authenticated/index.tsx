import { createFileRoute } from '@tanstack/react-router'
import { OverviewPage } from '@/features/daq/overview'

export const Route = createFileRoute('/_authenticated/')({
  component: OverviewPage,
})
