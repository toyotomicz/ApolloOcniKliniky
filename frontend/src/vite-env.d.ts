/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Optional absolute API origin. Leave empty to call the API on the same origin (/api). */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
