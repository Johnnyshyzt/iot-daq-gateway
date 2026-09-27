import { createFileRoute } from '@tanstack/react-router'
import { OpcUaPage } from '@/features/daq/opcua'

export const Route = createFileRoute('/_authenticated/integrate/opcua')({
  component: OpcUaPage,
})
