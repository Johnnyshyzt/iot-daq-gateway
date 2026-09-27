import { createFileRoute } from '@tanstack/react-router'
import { OeePage } from '@/features/oee/oee-page'

export const Route = createFileRoute('/_authenticated/oee')({
  component: OeePage,
})
