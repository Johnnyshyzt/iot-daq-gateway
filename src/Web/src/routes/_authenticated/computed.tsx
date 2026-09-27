import { createFileRoute } from '@tanstack/react-router'
import { ComputedPage } from '@/features/edge/computed-page'

export const Route = createFileRoute('/_authenticated/computed')({
  component: ComputedPage,
})
