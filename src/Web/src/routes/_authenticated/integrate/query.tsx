import { createFileRoute } from '@tanstack/react-router'
import { QueryApiPage } from '@/features/daq/query-api'

export const Route = createFileRoute('/_authenticated/integrate/query')({
  component: QueryApiPage,
})
