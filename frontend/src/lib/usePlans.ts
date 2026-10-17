import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { publicApi } from '../api/endpoints'
import type { Plan } from '../api/types'

// Plans are defined on the backend (PlanCatalog.cs) and rarely change; cache them per page load.
let cache: Plan[] | null = null

export function usePlans() {
  const [plans, setPlans] = useState<Plan[] | null>(cache)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      cache = await publicApi.plans()
      setPlans(cache)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Úrovně registrace se nepodařilo načíst.')
    }
  }, [])

  useEffect(() => {
    if (!cache) void load()
  }, [load])

  return { plans, error, retry: load }
}

/** For tests. */
export function resetPlansCache() {
  cache = null
}
