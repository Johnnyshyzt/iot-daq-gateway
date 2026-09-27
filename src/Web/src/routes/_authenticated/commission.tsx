import { createFileRoute } from '@tanstack/react-router'
import { CommissionPage } from '@/features/daq/commission'

export const Route = createFileRoute('/_authenticated/commission')({
  component: CommissionPage,
})
