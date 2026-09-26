import { createFileRoute } from '@tanstack/react-router'
import { CatalogPage } from '@/features/daq/catalog'

export const Route = createFileRoute('/_authenticated/catalog')({
  component: CatalogPage,
})
