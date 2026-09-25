import { createFileRoute } from '@tanstack/react-router'
import { PublishPage } from '@/features/daq/publish'

export const Route = createFileRoute('/_authenticated/publish')({
  component: PublishPage,
})
