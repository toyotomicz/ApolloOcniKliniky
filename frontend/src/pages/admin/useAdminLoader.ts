import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api/client'
import { useAdminSession } from '../../auth/sessions'

/** Loads admin data and sends the user back to the login page if the session has expired. */
export function useAdminLoader<T>(loader: () => Promise<T>, deps: readonly unknown[]) {
  const session = useAdminSession()
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const load = useCallback(loader, deps)

  const reload = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setData(await load())
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        session.markSignedOut()
        return
      }
      setError(e instanceof ApiError ? e.message : 'Data se nepodařilo načíst.')
    } finally {
      setLoading(false)
    }
  }, [load, session])

  useEffect(() => {
    void reload()
  }, [reload])

  return { data, setData, error, loading, reload }
}
