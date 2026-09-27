import { createFileRoute } from '@tanstack/react-router'
import { ToolsPage } from '@/features/shop/tools-page'

export const Route = createFileRoute('/_authenticated/tools')({
  component: ToolsPage,
})
