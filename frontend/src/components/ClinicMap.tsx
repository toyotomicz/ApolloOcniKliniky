import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useRef } from 'react'
import type { ClinicStatus } from '../api/types'
import { statusLabels } from '../lib/format'

export interface MapPoint {
  id: string
  name: string
  city: string
  status: ClinicStatus
  latitude: number
  longitude: number
}

interface ClinicMapProps {
  points: MapPoint[]
  /** Accessible name of the map region. */
  label: string
  height?: number | string
  /** Opens a clinic (e.g. navigates to its detail). Omit for a single-clinic preview without links. */
  onOpen?: (id: string) => void
  /** Zoom used when there is a single point. */
  singleZoom?: number
}

// Centre of the Czech Republic, used when there is nothing to fit.
const CZ_CENTER: L.LatLngTuple = [49.8, 15.47]

const statusToken: Record<ClinicStatus, string> = {
  pending: '--color-warning',
  approved: '--color-success',
  rejected: '--color-danger',
}

/**
 * OpenStreetMap map with one circle marker per clinic, coloured by registration status (always with a text legend
 * and the status in the popup, never colour alone). Popups are built with DOM APIs, so clinic names are never
 * interpreted as HTML.
 */
export function ClinicMap({ points, label, height = 480, onOpen, singleZoom = 14 }: ClinicMapProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<L.Map | null>(null)
  const layerRef = useRef<L.LayerGroup | null>(null)
  const openRef = useRef(onOpen)

  useEffect(() => {
    openRef.current = onOpen
  }, [onOpen])

  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    const map = L.map(container, { scrollWheelZoom: false, center: CZ_CENTER, zoom: 7 })
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    }).addTo(map)
    // Wheel zoom only after the map was focused/clicked, so the page scrolls normally over it.
    map.on('focus click', () => map.scrollWheelZoom.enable())
    map.on('blur mouseout', () => map.scrollWheelZoom.disable())
    mapRef.current = map
    layerRef.current = L.layerGroup().addTo(map)
    return () => {
      map.remove()
      mapRef.current = null
      layerRef.current = null
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    const layer = layerRef.current
    if (!map || !layer) return
    layer.clearLayers()
    const styles = getComputedStyle(document.documentElement)

    for (const point of points) {
      const color = styles.getPropertyValue(statusToken[point.status]).trim() || '#465051'
      const marker = L.circleMarker([point.latitude, point.longitude], {
        radius: 8,
        color: '#ffffff',
        weight: 2,
        fillColor: color,
        fillOpacity: 0.9,
      })

      const popup = document.createElement('div')
      popup.className = 'map-popup'
      const title = document.createElement('strong')
      title.textContent = point.name
      const meta = document.createElement('div')
      meta.textContent = `${point.city} · ${statusLabels[point.status]}`
      popup.append(title, meta)
      if (openRef.current) {
        const button = document.createElement('button')
        button.type = 'button'
        button.className = 'btn-link'
        button.textContent = 'Otevřít detail'
        button.addEventListener('click', () => openRef.current?.(point.id))
        popup.append(button)
      }
      marker.bindPopup(popup)
      marker.bindTooltip(point.name, { direction: 'top', offset: [0, -8] })
      layer.addLayer(marker)
    }

    if (points.length === 1) {
      map.setView([points[0]!.latitude, points[0]!.longitude], singleZoom)
    } else if (points.length > 1) {
      map.fitBounds(L.latLngBounds(points.map((p) => [p.latitude, p.longitude] as L.LatLngTuple)), { padding: [32, 32], maxZoom: 13 })
    } else {
      map.setView(CZ_CENTER, 7)
    }
  }, [points, singleZoom])

  return <div ref={containerRef} className="clinic-map" style={{ height }} role="region" aria-label={label} />
}

export function MapLegend() {
  return (
    <ul className="map-legend" aria-label="Legenda mapy">
      {(Object.keys(statusToken) as ClinicStatus[]).map((status) => (
        <li key={status}>
          <span className={`map-legend-dot map-legend-${status}`} aria-hidden="true" />
          {statusLabels[status]}
        </li>
      ))}
    </ul>
  )
}
