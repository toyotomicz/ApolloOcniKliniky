const RELOAD_KEY = 'apollodata:chunk-reload-at'
const RELOAD_WINDOW_MS = 10_000

/**
 * Loads a lazily imported route module. After a redeploy the hashed chunk names of the old build no longer
 * exist, so a tab opened before the deploy fails to import them; reloading once fetches the new build.
 * A second failure within a short window is rethrown to the route error boundary (no reload loop).
 */
export async function loadChunk<T>(load: () => Promise<T>): Promise<T> {
  try {
    return await load()
  } catch (error) {
    if (reloadedRecently()) throw error
    try {
      sessionStorage.setItem(RELOAD_KEY, String(Date.now()))
    } catch {
      throw error // Without storage we cannot guard against a loop.
    }
    window.location.reload()
    return new Promise<never>(() => {})
  }
}

function reloadedRecently(): boolean {
  try {
    const last = Number(sessionStorage.getItem(RELOAD_KEY))
    return Number.isFinite(last) && Date.now() - last < RELOAD_WINDOW_MS
  } catch {
    return false
  }
}
