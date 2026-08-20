export interface Category {
  id: number
  name: string
}

export interface PublishResult {
  success: boolean
  title?: string
  url?: string
  status?: string
  elapsedMs: number
  error?: string
}

export async function checkSession(): Promise<{ username: string } | null> {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin' })
  if (response.status === 401) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Session check failed: ${response.status}`)
  }
  return response.json()
}

export async function login(username: string, password: string): Promise<boolean> {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  })
  if (response.status === 401) {
    return false
  }
  if (!response.ok) {
    throw new Error(`Login request failed: ${response.status}`)
  }
  return true
}

export async function logout(): Promise<void> {
  await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' })
}

export async function fetchCategories(): Promise<Category[]> {
  const response = await fetch('/api/categories', { credentials: 'same-origin' })
  if (!response.ok) {
    throw new Error(`Failed to load categories: ${response.status}`)
  }
  return response.json()
}

export interface PublishArgs {
  rawText: string
  categoryId: number
  status: 'publish' | 'draft'
  featuredImageIndex: number
  images: File[]
  /** One entry per image, same order, '' where the operator left the credit blank. */
  captions: string[]
  /** false: title/subtitle come from the operator and the body is not rewritten. */
  useAi: boolean
  /** Required, and used, only when useAi is false. */
  title?: string
  subtitle?: string
}

export async function publishArticle(args: PublishArgs): Promise<PublishResult> {
  const form = new FormData()
  form.set('rawText', args.rawText)
  form.set('categoryId', String(args.categoryId))
  form.set('status', args.status)
  form.set('featuredImageIndex', String(args.featuredImageIndex))
  form.set('useAi', String(args.useAi))
  form.set('title', args.title ?? '')
  form.set('subtitle', args.subtitle ?? '')
  for (let i = 0; i < args.images.length; i++) {
    form.append('images', args.images[i], args.images[i].name)
    // Appended in lockstep with the file above; the server zips them by index.
    form.append('imageCaptions', args.captions[i] ?? '')
  }

  const response = await fetch('/api/publish', {
    method: 'POST',
    credentials: 'same-origin',
    body: form,
  })
  if (!response.ok) {
    throw new Error(`Publish request failed: ${response.status}`)
  }
  return response.json()
}
