import { createFileRoute } from '@tanstack/react-router'
import { RulesPage } from '@/features/edge/rules-page'

export const Route = createFileRoute('/_authenticated/rules')({
  component: RulesPage,
})
