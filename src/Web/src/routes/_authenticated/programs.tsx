import { createFileRoute } from '@tanstack/react-router'
import { ProgramsPage } from '@/features/shop/programs-page'

export const Route = createFileRoute('/_authenticated/programs')({
  component: ProgramsPage,
})
