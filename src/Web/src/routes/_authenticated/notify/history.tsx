import { createFileRoute } from '@tanstack/react-router'
import { NotifyHistoryPage } from '@/features/daq/notify-history'

export const Route = createFileRoute('/_authenticated/notify/history')({
  component: NotifyHistoryPage,
})
