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
    let poll = 0

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

    async function stream() {
      const token = useAuthStore.getState().auth.accessToken
      const headers = new Headers({ Accept: 'text/event-stream' })
      if (token) headers.set('Authorization', `Bearer ${token}`)
      try {
        const response = await fetch('/api/v1/live/stream', { headers, signal: controller.signal })
        if (!response.ok || !response.body) throw new Error('stream')
        if (!stop) setPush(true)
        const reader = response.body.getReader()
        const decoder = new TextDecoder()
        let buffer = ''
        while (!stop) {
          const chunk = await reader.read()
          if (chunk.done) break
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
      } catch {
        if (stop) return
        if (!stop) setPush(false)
        poll = window.setInterval(() => void load(), 3000)
      }
    }

    void load()
    void stream()
    return () => {
      stop = true
      controller.abort()
      window.clearInterval(poll)
    }
  }, [])

  return { data, error, push }
}
