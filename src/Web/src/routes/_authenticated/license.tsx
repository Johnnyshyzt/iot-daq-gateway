import { createFileRoute } from '@tanstack/react-router'
import { LicensePage } from '@/features/daq/license'

export const Route = createFileRoute('/_authenticated/license')({
  component: LicensePage,
})
