import { useEffect, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { studioApi } from '@/lib/studio-api'

export type LicenseSnapshot = {
  status: string
  edition: string
  message: string
  customer: string
  deviceLimit: number | null
  pointLimit: number | null
  draftDevices: number
  draftPoints: number
  publishedDevices: number
  publishedPoints: number
  expiresAt?: string | null
  graceUntil?: string | null
  issuedAt?: string | null
  graceDays: number
  features: string[]
  gatedFeatures: string[]
  entitlements: Record<string, boolean>
  machineFingerprint: string
  boundFingerprint?: string | null
  banner?: string | null
  collectionContinues: boolean
  demoMode: boolean
}

export function useLicense() {
  const [license, setLicense] = useState<LicenseSnapshot | null>(null)
  useEffect(() => {
    void studioApi<LicenseSnapshot>('/api/v1/license')
      .then(setLicense)
      .catch(() => setLicense(null))
  }, [])
  return license
}

export function LicenseBanner() {
  const license = useLicense()
  if (!license?.banner) return null
  return (
    <div className='border-b border-amber-500/40 bg-amber-500/10 px-4 py-2 text-sm text-amber-950 dark:text-amber-100'>
      <span className='font-medium'>授权：</span>
      {license.banner}{' '}
      <Link to='/license' className='underline'>
        打开授权许可
      </Link>
      {license.collectionContinues ? ' 采集仍在运行。' : null}
    </div>
  )
}

const titles: Record<string, string> = {
  opcua: 'OPC UA 服务器',
  'http-push': 'HTTP 推送',
  'alarm-notifications': '报警通知',
  'scheduled-reports': '定时报表',
  'query-api': '只读查询接口',
}

export function FeatureLock({ feature }: { feature: string }) {
  const license = useLicense()
  if (!license || license.entitlements[feature] !== false) return null
  return (
    <div className='mb-4 rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm'>
      当前授权未包含{titles[feature] ?? feature}。采集不会停止。请到
      <Link to='/license' className='mx-1 underline'>
        授权许可
      </Link>
      导入许可证，或在配置的功能清单里调整。
    </div>
  )
}
