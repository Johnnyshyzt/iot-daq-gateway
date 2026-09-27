import { createFileRoute } from '@tanstack/react-router'
import { SetupPage } from '@/features/daq/onboarding'

export const Route = createFileRoute('/_authenticated/setup')({
  component: SetupPage,
})
