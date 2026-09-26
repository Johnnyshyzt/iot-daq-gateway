import { createFileRoute } from '@tanstack/react-router'
import { DeviceDetailPage } from '@/features/viz/device-detail'

export const Route = createFileRoute('/_authenticated/monitor/$deviceId')({
  component: function MonitorRoute() {
    const { deviceId } = Route.useParams()
    return <DeviceDetailPage deviceId={deviceId} />
  },
})
