import { createFileRoute } from '@tanstack/react-router'
import { NotifyPage } from '@/features/daq/notify'

export const Route = createFileRoute('/_authenticated/notify/')({
  component: NotifyPage,
})
