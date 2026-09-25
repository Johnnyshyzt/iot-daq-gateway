export async function api(path, options = {}) {
  const headers = { ...(options.headers || {}) }
  if (options.body !== undefined && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }

  const token = sessionStorage.getItem('studio_token')
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }

  const response = await fetch(path, { ...options, headers })
  if (response.status === 401 && !path.includes('/auth/login')) {
    sessionStorage.removeItem('studio_token')
    sessionStorage.removeItem('studio_user')
    sessionStorage.removeItem('studio_role')
    if (!location.pathname.endsWith('/login')) {
      location.assign('/login')
    }
    throw Object.assign(new Error('需要登录'), { status: 401 })
  }

  if (response.status === 204) {
    return null
  }

  const text = await response.text()
  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = { message: text }
    }
  }

  if (!response.ok) {
    const error = new Error(data?.message || response.statusText)
    error.status = response.status
    error.code = data?.code
    error.details = data?.details
    throw error
  }

  return data
}
