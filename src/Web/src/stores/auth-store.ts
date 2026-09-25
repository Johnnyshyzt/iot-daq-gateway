import { create } from 'zustand'
import { getCookie, setCookie, removeCookie } from '@/lib/cookies'

const ACCESS_TOKEN = 'thisisjustarandomstring'
const MUST_CHANGE = 'studio.mustChangePassword'

export function persistMustChange(value: boolean | undefined) {
  try {
    if (value) sessionStorage.setItem(MUST_CHANGE, '1')
    else sessionStorage.removeItem(MUST_CHANGE)
  } catch {
    // sessionStorage can be unavailable in private modes
  }
}

export function readMustChange() {
  try {
    return sessionStorage.getItem(MUST_CHANGE) === '1'
  } catch {
    return false
  }
}

interface AuthUser {
  accountNo: string
  email: string
  role: string[]
  exp: number
  mustChangePassword?: boolean
}

interface AuthState {
  auth: {
    user: AuthUser | null
    setUser: (user: AuthUser | null) => void
    accessToken: string
    setAccessToken: (accessToken: string) => void
    resetAccessToken: () => void
    reset: () => void
  }
}

export const useAuthStore = create<AuthState>()((set) => {
  const cookieState = getCookie(ACCESS_TOKEN)
  const initToken = cookieState ? JSON.parse(cookieState) : ''
  return {
    auth: {
      user: null,
      setUser: (user) =>
        set((state) => {
          persistMustChange(user?.mustChangePassword)
          return { ...state, auth: { ...state.auth, user } }
        }),
      accessToken: initToken,
      setAccessToken: (accessToken) =>
        set((state) => {
          setCookie(ACCESS_TOKEN, JSON.stringify(accessToken))
          return { ...state, auth: { ...state.auth, accessToken } }
        }),
      resetAccessToken: () =>
        set((state) => {
          removeCookie(ACCESS_TOKEN)
          return { ...state, auth: { ...state.auth, accessToken: '' } }
        }),
      reset: () =>
        set((state) => {
          removeCookie(ACCESS_TOKEN)
          persistMustChange(false)
          return {
            ...state,
            auth: { ...state.auth, user: null, accessToken: '' },
          }
        }),
    },
  }
})
