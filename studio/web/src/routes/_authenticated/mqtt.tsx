import { createFileRoute } from '@tanstack/react-router'
import { MqttPage } from '@/features/daq/mqtt'

export const Route = createFileRoute('/_authenticated/mqtt')({
  component: MqttPage,
})
