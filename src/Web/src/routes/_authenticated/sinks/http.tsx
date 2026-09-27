import { createFileRoute } from '@tanstack/react-router'
import { HttpPushPage } from '@/features/daq/http-push'

export const Route = createFileRoute('/_authenticated/sinks/http')({
  component: HttpPushPage,
})
