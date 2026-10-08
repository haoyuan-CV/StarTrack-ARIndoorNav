import json
import math
from pathlib import Path

import networkx as nx


def load_topology(json_path):
    """Load topology.json and build an undirected weighted graph."""
    with open(json_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    graph = nx.Graph()

    for name, coords in data["nodes"].items():
        graph.add_node(name, pos=(coords["x"], coords["y"], coords["z"]))

    for edge in data["edges"]:
        node1 = graph.nodes[edge["from"]]["pos"]
        node2 = graph.nodes[edge["to"]]["pos"]
        dist = math.sqrt(
            (node1[0] - node2[0]) ** 2
            + (node1[1] - node2[1]) ** 2
            + (node1[2] - node2[2]) ** 2
        )
        graph.add_edge(edge["from"], edge["to"], weight=dist)

    return graph


def find_nearest_node(graph, current_pose):
    """Find the topology node nearest to the current HLoc pose."""
    min_dist = float("inf")
    nearest_node = None

    for node, attr in graph.nodes(data=True):
        pos = attr["pos"]
        dist = math.sqrt(
            (pos[0] - current_pose[0]) ** 2
            + (pos[1] - current_pose[1]) ** 2
            + (pos[2] - current_pose[2]) ** 2
        )
        if dist < min_dist:
            min_dist = dist
            nearest_node = node

    return nearest_node


def get_navigation_path(current_pose, dest_name, topology_path=None):
    """Return path coordinates from current pose to destination node."""
    if topology_path is None:
        topology_path = Path(__file__).resolve().parent / "topology.json"

    graph = load_topology(topology_path)
    start_node = find_nearest_node(graph, current_pose)
    path_nodes = nx.shortest_path(graph, source=start_node, target=dest_name, weight="weight")
    path_coordinates = [graph.nodes[node]["pos"] for node in path_nodes]
    path_coordinates.insert(0, tuple(current_pose))
    return path_coordinates


if __name__ == "__main__":
    current_hloc_pose = [-0.600, -0.440, 0.100]
    path = get_navigation_path(current_hloc_pose, "Discussion Area 1")
    print("Path returned to frontend:", path)
