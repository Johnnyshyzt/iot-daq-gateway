import { useEffect, useState } from 'react'
import { describeError, studioApi } from '@/lib/studio-api'
import { useAuthStore } from '@/stores/auth-store'
import type { DashboardSnapshot } from './types'

export function useOverview() {
  const [data, setData] = useState<DashboardSnapshot | null>(null)
  const [error, setError] = useState('')
  const [push, setPush] = useState(false)

  useEffect(() => {
    let stop = false
    const controller = new AbortController()

    async function load() {
      try {
        const next = await studioApi<DashboardSnapshot>('/api/v1/dashboard/overview')
        if (!stop) {
          setData(next)
          setError('')
        }
      } catch (err) {
        if (!stop) setError(describeError(err))
      }
    }

    async function readStream() {
      const token = useAuthStore.getState().auth.accessToken
      const headers = new Headers({ Accept: 'text/event-stream' })
      if (token) headers.set('Authorization', `Bearer ${token}`)
      const response = await fetch('/api/v1/live/stream', { headers, signal: controller.signal })
      if (!response.ok || !response.body) throw new Error('stream')
      if (!stop) setPush(true)
      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      while (!stop) {
        const chunk = await reader.read()
        if (chunk.done) return
        buffer += decoder.decode(chunk.value, { stream: true })
        const parts = buffer.split('\n\n')
        buffer = parts.pop() ?? ''
        for (const part of parts) {
          const line = part.split('\n').find((item) => item.startsWith('data:'))
          if (!line) continue
          try {
            const next = JSON.parse(line.slice(5).trim()) as DashboardSnapshot
            if (!stop) {
              setData(next)
              setError('')
            }
          } catch {
            // ignore a partial frame
          }
        }
      }
    }

    async function loop() {
      await load()
      let delay = 1000
      while (!stop) {
        try {
          await readStream()
          if (stop) return
          delay = 1000
        } catch {
          if (stop || controller.signal.aborted) return
          if (!stop) setPush(false)
        }
        if (stop) return
        await load()
        await new Promise((resolve) => window.setTimeout(resolve, delay))
        delay = Math.min(delay * 2, 15_000)
      }
    }

    void loop()
    return () => {
      stop = true
      controller.abort()
    }
  }, [])

  return { data, error, push }
}
