import { createFileRoute } from '@tanstack/react-router'
import { AuditPage } from '@/features/daq/audit'

export const Route = createFileRoute('/_authenticated/audit')({
  component: AuditPage,
})
