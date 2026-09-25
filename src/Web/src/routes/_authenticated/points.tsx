import { createFileRoute } from '@tanstack/react-router'
import { PointsPage } from '@/features/daq/points'

export const Route = createFileRoute('/_authenticated/points')({
  component: PointsPage,
})
