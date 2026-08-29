import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { applyRouteMetadata } from './seo'
import { page as recordPageView } from './analytics'

/**
 * Views are lazily loaded so the landing page — the only one most visitors see — does not carry
 * the weight of pdf.js, which the form designer needs but nothing else does.
 */
const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: () => import('./views/HomeView.vue') },
  { path: '/create', name: 'create', component: () => import('./views/CreateTextView.vue') },
  { path: '/word', name: 'word', component: () => import('./views/WordView.vue') },
  { path: '/edit', name: 'edit', component: () => import('./views/EditTextView.vue') },
  { path: '/merge', name: 'merge', component: () => import('./views/MergeView.vue') },
  { path: '/pages', name: 'pages', component: () => import('./views/PagesView.vue') },
  { path: '/forms', name: 'forms', component: () => import('./views/FormsView.vue') },
  { path: '/summarize', name: 'summarize', component: () => import('./views/SummarizeView.vue') },
  { path: '/inspect', name: 'inspect', component: () => import('./views/InspectView.vue') },
  { path: '/api', name: 'api', component: () => import('./views/ApiView.vue') },

  { path: '/contact', name: 'contact', component: () => import('./views/ContactView.vue') },
  { path: '/privacy', name: 'privacy', component: () => import('./views/PrivacyView.vue') },
  { path: '/terms', name: 'terms', component: () => import('./views/TermsView.vue') },

  // Not in the navigation, and not in the sitemap. Reachable by anyone who types it, but there is
  // nothing behind it without an administrator's key, so hiding the route is tidiness rather than
  // a security measure — the server is what refuses.
  { path: '/admin', name: 'admin', component: () => import('./views/AdminView.vue') },
  { path: '/:catchAll(.*)', redirect: '/' },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

// After the navigation, not before: a title that changes while the old page is still on screen
// describes something the reader is not looking at yet.
router.afterEach((to) => {
  applyRouteMetadata(to.path)

  // After the title is set, so the page view carries the right one. Does nothing at all until
  // the visitor has accepted analytics.
  recordPageView(to.path)
})
