(() => {
  "use strict";

  const root = document.querySelector("[data-repository-graph]");
  if (!root) {
    return;
  }

  const vendor = window.SvnHubGraphVendor;
  const loading = root.querySelector("[data-graph-loading]");
  const empty = root.querySelector("[data-graph-empty]");
  if (!vendor) {
    loading.hidden = true;
    empty.textContent = "Repository graph components could not be loaded.";
    empty.hidden = false;
    return;
  }

  const dataElement = document.getElementById("repositoryGraphData");
  const canvas = root.querySelector("[data-graph-canvas]");
  const stats = root.querySelector("[data-graph-stats]");
  const search = root.querySelector("[data-graph-search]");
  const names = document.getElementById("repositoryGraphNames");
  const hideIsolatedInput = root.querySelector("[data-graph-hide-isolated]");
  const details = root.querySelector("[data-graph-details]");
  const detailName = root.querySelector("[data-graph-detail-name]");
  const detailLabels = root.querySelector("[data-graph-detail-labels]");
  const detailIncoming = root.querySelector("[data-graph-detail-incoming]");
  const detailOutgoing = root.querySelector("[data-graph-detail-outgoing]");
  const detailIncomingReferences = root.querySelector("[data-graph-detail-incoming-references]");
  const detailOutgoingReferences = root.querySelector("[data-graph-detail-outgoing-references]");
  const detailSelfContainer = root.querySelector("[data-graph-detail-self-container]");
  const detailSelf = root.querySelector("[data-graph-detail-self]");
  const detailOpen = root.querySelector("[data-graph-detail-open]");
  const detailReferences = root.querySelector("[data-graph-detail-references]");
  const detailFocus = root.querySelector("[data-graph-detail-focus]");

  let data;
  try {
    data = JSON.parse(dataElement.textContent);
  } catch {
    showFailure("Repository graph data could not be read.");
    return;
  }

  const { Graph, Sigma, EdgeArrowProgram, forceAtlas2, FA2Layout } = vendor;
  const graph = new Graph({ type: "directed", multi: false, allowSelfLoops: false });
  const nodesById = new Map(data.nodes.map((node) => [node.id, node]));
  const state = {
    pinning: "all",
    hideIsolated: false,
    selectedNode: null,
    neighborhoodOnly: false,
    visibleNodes: new Set(),
    visibleEdges: new Set(),
    activeReferenceCount: 0,
  };
  let palette = readPalette();
  let activeLayout = null;
  let layoutSyncTimer = null;
  let layoutStopTimer = null;

  data.nodes.forEach((node, index) => {
    const angle = index * 2.399963229728653;
    const radius = 1 + Math.sqrt(index + 1);
    const degree = node.incomingReferenceCount + node.outgoingReferenceCount;
    graph.addNode(node.id, {
      x: Math.cos(angle) * radius,
      y: Math.sin(angle) * radius,
      size: degree === 0 ? 5.5 : Math.min(13, 3.5 + Math.log2(degree + 1) * 1.35),
      label: node.name,
      data: node,
    });

    const option = document.createElement("option");
    option.value = node.name;
    names.appendChild(option);
  });

  data.edges.forEach((edge) => {
    const key = `${edge.source}:${edge.target}`;
    graph.addDirectedEdgeWithKey(key, edge.source, edge.target, {
      type: "arrow",
      size: Math.min(7, 0.7 + Math.log2(edge.referenceCount + 1) * 0.75),
      weight: Math.max(1, Math.log2(edge.referenceCount + 1)),
      data: edge,
    });
  });

  if (graph.order === 0) {
    loading.hidden = true;
    empty.hidden = false;
    return;
  }

  let renderer;
  try {
    renderer = new Sigma(graph, canvas, {
      defaultEdgeType: "arrow",
      edgeProgramClasses: { arrow: EdgeArrowProgram },
      nodeReducer,
      edgeReducer,
      labelDensity: 0.08,
      labelGridCellSize: 110,
      labelRenderedSizeThreshold: 8,
      minCameraRatio: 0.025,
      maxCameraRatio: 12,
      zIndex: true,
    });
  } catch {
    showFailure("WebGL repository graph could not be initialized.");
    return;
  }

  recomputeVisibility();
  renderer.refresh();
  arrangeGraph();

  renderer.on("clickNode", ({ node }) => selectNode(node, false));
  renderer.on("doubleClickNode", ({ node, preventSigmaDefault }) => {
    preventSigmaDefault();
    window.location.assign(nodesById.get(node).repositoryUrl);
  });
  renderer.on("clickStage", () => clearSelection());
  renderer.on("enterNode", () => {
    canvas.style.cursor = "pointer";
  });
  renderer.on("leaveNode", () => {
    canvas.style.cursor = "default";
  });

  root.querySelectorAll("[data-graph-pinning]").forEach((input) => {
    input.addEventListener("change", () => {
      if (!input.checked) {
        return;
      }

      state.pinning = input.value;
      state.neighborhoodOnly = false;
      detailFocus.textContent = "Focus neighborhood";
      recomputeVisibility();
      renderer.refresh();
    });
  });

  hideIsolatedInput.addEventListener("change", () => {
    state.hideIsolated = hideIsolatedInput.checked;
    recomputeVisibility();
    renderer.refresh();
  });

  root.querySelector("[data-graph-fit]").addEventListener("click", () => {
    fitCamera(400);
  });

  search.addEventListener("keydown", (event) => {
    if (event.key !== "Enter") {
      return;
    }

    event.preventDefault();
    selectSearchResult();
  });
  search.addEventListener("change", selectSearchResult);

  root.querySelector("[data-graph-detail-close]").addEventListener("click", clearSelection);
  detailFocus.addEventListener("click", () => {
    if (!state.selectedNode) {
      return;
    }

    state.neighborhoodOnly = !state.neighborhoodOnly;
    detailFocus.textContent = state.neighborhoodOnly ? "Show all" : "Focus neighborhood";
    recomputeVisibility();
    renderer.refresh();
    focusCamera(state.selectedNode);
  });

  const themeObserver = new MutationObserver(() => {
    palette = readPalette();
    renderer.refresh();
  });
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });

  window.addEventListener("beforeunload", () => {
    themeObserver.disconnect();
    disposeLayout();
    renderer.kill();
  }, { once: true });

  function edgeReferenceCount(edge) {
    if (state.pinning === "pinned") {
      return edge.pinnedReferenceCount;
    }
    if (state.pinning === "unpinned") {
      return edge.unpinnedReferenceCount;
    }
    return edge.referenceCount;
  }

  function recomputeVisibility() {
    const activeEdges = new Set();
    const connectedNodes = new Set();
    let activeReferences = 0;

    graph.forEachEdge((edgeKey, attributes, source, target) => {
      const count = edgeReferenceCount(attributes.data);
      if (count <= 0) {
        return;
      }

      activeEdges.add(edgeKey);
      connectedNodes.add(source);
      connectedNodes.add(target);
      activeReferences += count;
    });

    let visibleNodes = state.hideIsolated
      ? connectedNodes
      : new Set(graph.nodes());
    let visibleEdges = activeEdges;

    if (state.neighborhoodOnly && state.selectedNode) {
      const neighborhood = new Set([state.selectedNode]);
      const neighborhoodEdges = new Set();
      activeEdges.forEach((edgeKey) => {
        const source = graph.source(edgeKey);
        const target = graph.target(edgeKey);
        if (source === state.selectedNode || target === state.selectedNode) {
          neighborhood.add(source);
          neighborhood.add(target);
          neighborhoodEdges.add(edgeKey);
        }
      });
      visibleNodes = neighborhood;
      visibleEdges = neighborhoodEdges;
      activeReferences = Array.from(visibleEdges).reduce(
        (sum, edgeKey) => sum + edgeReferenceCount(graph.getEdgeAttribute(edgeKey, "data")),
        0);
    }

    state.visibleNodes = visibleNodes;
    state.visibleEdges = visibleEdges;
    state.activeReferenceCount = activeReferences;
    empty.hidden = visibleNodes.size > 0;
    stats.textContent = `${visibleNodes.size} ${pluralize(visibleNodes.size, "repository", "repositories")} \u00b7 ` +
      `${visibleEdges.size} ${pluralize(visibleEdges.size, "connection", "connections")} \u00b7 ` +
      `${activeReferences} ${pluralize(activeReferences, "reference", "references")}`;
  }

  function nodeReducer(node, attributes) {
    const result = {
      ...attributes,
      hidden: !state.visibleNodes.has(node),
      color: graph.degree(node) === 0 ? palette.isolatedNode : palette.node,
    };
    if (state.visibleNodes.size <= 25) {
      result.forceLabel = true;
    }
    if (!state.selectedNode) {
      return result;
    }

    if (node === state.selectedNode) {
      result.color = palette.selected;
      result.forceLabel = true;
      result.highlighted = true;
      result.zIndex = 3;
      return result;
    }

    const incoming = graph.hasDirectedEdge(node, state.selectedNode);
    const outgoing = graph.hasDirectedEdge(state.selectedNode, node);
    if (incoming && outgoing) {
      result.color = palette.both;
      result.zIndex = 2;
    } else if (incoming) {
      result.color = palette.incoming;
      result.zIndex = 2;
    } else if (outgoing) {
      result.color = palette.outgoing;
      result.zIndex = 2;
    } else {
      result.color = palette.mutedNode;
    }
    return result;
  }

  function edgeReducer(edgeKey, attributes) {
    const edge = attributes.data;
    const result = {
      ...attributes,
      hidden: !state.visibleEdges.has(edgeKey),
      color: edgeColor(edge),
      size: Math.min(7, 0.7 + Math.log2(edgeReferenceCount(edge) + 1) * 0.75),
      zIndex: 1,
    };

    if (state.selectedNode) {
      const source = graph.source(edgeKey);
      const target = graph.target(edgeKey);
      if (source !== state.selectedNode && target !== state.selectedNode) {
        result.color = palette.mutedEdge;
        result.zIndex = 0;
      } else {
        result.size *= 1.4;
        result.zIndex = 2;
      }
    }
    return result;
  }

  function edgeColor(edge) {
    if (state.pinning === "pinned") {
      return palette.pinned;
    }
    if (state.pinning === "unpinned") {
      return palette.unpinned;
    }
    if (edge.pinnedReferenceCount > 0 && edge.unpinnedReferenceCount > 0) {
      return palette.mixed;
    }
    return edge.pinnedReferenceCount > 0 ? palette.pinned : palette.unpinned;
  }

  function selectNode(nodeId, focus) {
    const node = nodesById.get(nodeId);
    if (!node) {
      return;
    }

    if (!state.visibleNodes.has(nodeId)) {
      state.hideIsolated = false;
      hideIsolatedInput.checked = false;
    }
    state.selectedNode = nodeId;
    state.neighborhoodOnly = false;
    detailFocus.textContent = "Focus neighborhood";
    showDetails(node);
    recomputeVisibility();
    renderer.refresh();
    if (focus) {
      focusCamera(nodeId);
    }
  }

  function clearSelection() {
    state.selectedNode = null;
    state.neighborhoodOnly = false;
    details.hidden = true;
    detailFocus.textContent = "Focus neighborhood";
    recomputeVisibility();
    renderer.refresh();
  }

  function showDetails(node) {
    detailName.textContent = node.name;
    detailLabels.replaceChildren(...node.labels.map((label) => {
      const badge = document.createElement("span");
      badge.className = "badge text-bg-secondary";
      badge.textContent = label;
      return badge;
    }));
    detailIncoming.textContent = node.incomingRepositoryCount;
    detailOutgoing.textContent = node.outgoingRepositoryCount;
    detailIncomingReferences.textContent = node.incomingReferenceCount;
    detailOutgoingReferences.textContent = node.outgoingReferenceCount;
    detailSelf.textContent = node.selfReferenceCount;
    detailSelfContainer.hidden = node.selfReferenceCount === 0;
    detailOpen.href = node.repositoryUrl;
    detailReferences.href = node.externalReferencesUrl;
    details.hidden = false;
  }

  function selectSearchResult() {
    const query = search.value.trim();
    if (!query) {
      return;
    }

    const normalized = query.toLocaleLowerCase();
    const exact = data.nodes.find((node) => node.name.toLocaleLowerCase() === normalized);
    const prefix = data.nodes.find((node) => node.name.toLocaleLowerCase().startsWith(normalized));
    const partial = data.nodes.find((node) => node.name.toLocaleLowerCase().includes(normalized));
    const result = exact ?? prefix ?? partial;
    if (result) {
      search.value = result.name;
      selectNode(result.id, true);
    }
  }

  function focusCamera(nodeId) {
    const displayData = renderer.getNodeDisplayData(nodeId);
    if (!displayData) {
      return;
    }

    renderer.getCamera().animate(
      { x: displayData.x, y: displayData.y, ratio: 0.16 },
      { duration: 450 });
  }

  function arrangeGraph() {
    const connectedNodes = new Set();
    graph.forEachEdge((_edge, _attributes, source, target) => {
      connectedNodes.add(source);
      connectedNodes.add(target);
    });

    if (connectedNodes.size < 2) {
      placeIsolatedNodes(connectedNodes);
      loading.hidden = true;
      renderer.refresh();
      fitCamera(300);
      return;
    }

    const layoutGraph = graph.copy();
    layoutGraph.nodes().forEach((node) => {
      if (!connectedNodes.has(node)) {
        layoutGraph.dropNode(node);
      }
    });

    const inferred = forceAtlas2.inferSettings(layoutGraph);
    const layout = new FA2Layout(layoutGraph, {
      getEdgeWeight: "weight",
      settings: {
        ...inferred,
        barnesHutOptimize: true,
        edgeWeightInfluence: 0.7,
        gravity: 1.2,
        scalingRatio: Math.max(2, inferred.scalingRatio),
        strongGravityMode: true,
        slowDown: Math.max(2, inferred.slowDown),
      },
    });
    activeLayout = layout;

    const syncPositions = () => {
      graph.updateEachNodeAttributes((node, attributes) => {
        if (!layoutGraph.hasNode(node)) {
          return attributes;
        }
        return {
          ...attributes,
          x: layoutGraph.getNodeAttribute(node, "x"),
          y: layoutGraph.getNodeAttribute(node, "y"),
        };
      }, { attributes: ["x", "y"] });
    };

    layout.start();
    layoutSyncTimer = window.setInterval(syncPositions, 250);
    layoutStopTimer = window.setTimeout(() => {
      window.clearInterval(layoutSyncTimer);
      layoutSyncTimer = null;
      layout.stop();
      syncPositions();
      placeIsolatedNodes(connectedNodes);
      layout.kill();
      activeLayout = null;
      layoutStopTimer = null;
      loading.hidden = true;
      renderer.refresh();
      fitCamera(500);
    }, 3500);
  }

  function placeIsolatedNodes(connectedNodes) {
    const isolated = graph.nodes().filter((node) => !connectedNodes.has(node));
    if (isolated.length === 0) {
      return;
    }

    let maxRadius = 1;
    connectedNodes.forEach((node) => {
      const x = graph.getNodeAttribute(node, "x");
      const y = graph.getNodeAttribute(node, "y");
      maxRadius = Math.max(maxRadius, Math.hypot(x, y));
    });
    const radius = maxRadius * 1.25;
    isolated.forEach((node, index) => {
      const angle = (index / isolated.length) * Math.PI * 2;
      graph.mergeNodeAttributes(node, {
        x: Math.cos(angle) * radius,
        y: Math.sin(angle) * radius,
      });
    });
  }

  function readPalette() {
    const dark = document.documentElement.getAttribute("data-bs-theme") === "dark";
    return dark
      ? {
          node: "#93a9b5",
          isolatedNode: "#718995",
          mutedNode: "#49575e",
          selected: "#6ea8fe",
          incoming: "#75b798",
          outgoing: "#ffb86b",
          both: "#b49ada",
          pinned: "#6ea8fe",
          unpinned: "#ffb02e",
          mixed: "#a98eda",
          mutedEdge: "#354149",
        }
      : {
          node: "#617d8a",
          isolatedNode: "#78919c",
          mutedNode: "#c2cbd0",
          selected: "#0d6efd",
          incoming: "#198754",
          outgoing: "#d66b00",
          both: "#7656a8",
          pinned: "#397dcc",
          unpinned: "#d88400",
          mixed: "#7656a8",
          mutedEdge: "#d4dadd",
        };
  }

  function pluralize(count, singular, plural) {
    return count === 1 ? singular : plural;
  }

  function fitCamera(duration) {
    const sparse = state.visibleEdges.size === 0 && state.visibleNodes.size <= 25;
    renderer.getCamera().animate(
      { x: 0.5, y: 0.5, ratio: sparse ? 2.1 : 1, angle: 0 },
      { duration });
  }

  function showFailure(message) {
    loading.hidden = true;
    empty.textContent = message;
    empty.hidden = false;
  }

  function disposeLayout() {
    if (layoutSyncTimer !== null) {
      window.clearInterval(layoutSyncTimer);
      layoutSyncTimer = null;
    }
    if (layoutStopTimer !== null) {
      window.clearTimeout(layoutStopTimer);
      layoutStopTimer = null;
    }
    if (activeLayout) {
      activeLayout.kill();
      activeLayout = null;
    }
  }
})();
