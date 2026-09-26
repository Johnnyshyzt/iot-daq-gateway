import { createFileRoute } from '@tanstack/react-router'
import { LivePage } from '@/features/daq/live'

type LiveSearch = { device: string }

export const Route = createFileRoute('/_authenticated/live')({
  validateSearch: (search: Record<string, unknown>): LiveSearch => ({
    device: typeof search.device === 'string' ? search.device : '',
  }),
  component: LiveRoute,
})

function LiveRoute() {
  const { device } = Route.useSearch()
  return <LivePage initialDeviceId={device} />
}
