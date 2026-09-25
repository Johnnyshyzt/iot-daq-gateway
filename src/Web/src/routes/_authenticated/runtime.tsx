import { createFileRoute } from '@tanstack/react-router'
import { RuntimePage } from '@/features/daq/runtime'

export const Route = createFileRoute('/_authenticated/runtime')({
  component: RuntimePage,
})
