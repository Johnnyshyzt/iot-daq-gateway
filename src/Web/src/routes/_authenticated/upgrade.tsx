import { createFileRoute } from '@tanstack/react-router'
import { UpgradePage } from '@/features/daq/upgrade'

export const Route = createFileRoute('/_authenticated/upgrade')({
  component: UpgradePage,
})
