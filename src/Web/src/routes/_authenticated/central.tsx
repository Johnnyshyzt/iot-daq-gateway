import { createFileRoute } from '@tanstack/react-router'
import { CentralPage } from '@/features/central/central-page'

export const Route = createFileRoute('/_authenticated/central')({
  component: CentralPage,
})
