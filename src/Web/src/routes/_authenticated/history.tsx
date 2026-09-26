import { createFileRoute } from '@tanstack/react-router'
import { HistoryPage } from '@/features/viz/history-page'

type HistorySearch = { device: string }

export const Route = createFileRoute('/_authenticated/history')({
  validateSearch: (search: Record<string, unknown>): HistorySearch => ({
    device: typeof search.device === 'string' ? search.device : '',
  }),
  component: function HistoryRoute() {
    const { device } = Route.useSearch()
    return <HistoryPage initialDeviceId={device} />
  },
})
