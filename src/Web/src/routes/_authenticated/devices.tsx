import { createFileRoute } from '@tanstack/react-router'
import { DevicesPage } from '@/features/daq/devices'

export const Route = createFileRoute('/_authenticated/devices')({
  component: DevicesPage,
})
