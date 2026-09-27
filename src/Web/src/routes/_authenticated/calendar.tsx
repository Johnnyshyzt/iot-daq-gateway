import { createFileRoute } from '@tanstack/react-router'
import { CalendarPage } from '@/features/oee/calendar-page'

export const Route = createFileRoute('/_authenticated/calendar')({
  component: CalendarPage,
})
