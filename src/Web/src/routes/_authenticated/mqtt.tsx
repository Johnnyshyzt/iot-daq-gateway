import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authenticated/mqtt')({
  beforeLoad: () => {
    throw redirect({ to: '/sinks/mqtt' })
  },
})
